using System.Text.Json;
using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.Services;
public sealed class BatchProcessor(IDbContextFactory<FinanceDbContext> factory, ITelegramClient telegram, IVoiceTranscriptionService transcription, IAiExtractionService extraction, FinanceOptions options, ILogger<BatchProcessor> logger)
{
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
 public async Task ProcessDueAsync(CancellationToken ct)
 {
  await using var db=await factory.CreateDbContextAsync(ct); var now=DateTimeOffset.UtcNow; var cutoff=now.AddSeconds(-options.MessageBatchDelaySeconds); var abandonedBefore=now.AddMinutes(-15);
  // SQLite cannot translate DateTimeOffset range comparisons. Filter by status in
  // SQL, then apply the timestamp checks to the small candidate set in memory.
  var candidates=await db.InputBatches.Where(x=>x.Status==BatchStatus.Collecting||x.Status==BatchStatus.Processing).ToListAsync(ct);
  var ids=candidates.Where(x=>(x.Status==BatchStatus.Collecting && x.LastMessageAt<=cutoff)||(x.Status==BatchStatus.Processing && x.ProcessingStartedAt<abandonedBefore)).Select(x=>x.Id).Take(10).ToList();
  foreach(var id in ids) await ProcessAsync(id,ct);
 }
 private async Task ProcessAsync(long id,CancellationToken ct)
 {
  await using var db=await factory.CreateDbContextAsync(ct); var batch=await db.InputBatches.Include(x=>x.User).Include(x=>x.Messages).SingleAsync(x=>x.Id==id,ct);
  if(batch.Status==BatchStatus.Collecting && batch.LastMessageAt>DateTimeOffset.UtcNow.AddSeconds(-options.MessageBatchDelaySeconds)) return;
  batch.Status=BatchStatus.Processing; batch.ProcessingStartedAt=DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
  AiRun? run=null;
  try
  {
   var texts=batch.Messages.Where(x=>!string.IsNullOrWhiteSpace(x.Text)).Select(x=>x.Text!).ToList(); var images=new List<string>();
   foreach(var message in batch.Messages.Where(x=>x.Type!=InputMessageType.Text && x.TelegramFileId!=null)) { var file=await telegram.DownloadAsync(message.TelegramFileId!,ct); if(message.Type==InputMessageType.Voice){message.Transcript=await transcription.TranscribeAsync(file,ct);texts.Add(message.Transcript);} else images.Add($"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}"); }
   var accounts=await db.Accounts.Where(x=>x.IsActive&&(x.OwnerUserId==null||x.OwnerUserId==batch.UserId)).Select(x=>new{x.Id,x.Name,x.CurrencyCode}).ToListAsync(ct); var categories=await db.Categories.Where(x=>x.IsActive).Select(x=>new{x.Id,x.Name,x.ParentId,x.Type}).ToListAsync(ct); var context=JsonSerializer.Serialize(new{accounts,categories,defaultCurrency=options.DefaultCurrency});
   run=new AiRun{InputBatchId=id,Model=options.OpenAiModel,InputJson=JsonSerializer.Serialize(new{texts,imageCount=images.Count,context}),StartedAt=DateTimeOffset.UtcNow,Status=RunStatus.Running}; db.AiRuns.Add(run); await db.SaveChangesAsync(ct);
   logger.LogDebug("Database write: AiRunId={AiRunId}, InputBatchId={InputBatchId}, Model={Model}, InputJson={InputJson}, Status={Status}",run.Id,run.InputBatchId,run.Model,run.InputJson,run.Status);
   var output=await extraction.ExtractAsync(new(texts,images,context),ct); logger.LogDebug("OpenAI extracted operation for InputBatchId={InputBatchId}: {Output}",id,output); run.OutputJson=output; var operation=JsonSerializer.Deserialize<ExtractedOperation>(output,JsonOptions) ?? throw new InvalidDataException("Empty AI operation.");
   var validator=new ValidationService(options.ValidationRoundingTolerance); var checks=validator.Validate(operation).ToList();
   Category? category=operation.CategoryId is long categoryId ? await db.Categories.Include(x=>x.Parent).SingleOrDefaultAsync(x=>x.Id==categoryId && x.IsActive,ct) : null;
   if(operation.Kind is "Expense" or "Income") checks.Add(new("Category",1,category is null?0:1,category is not null,"A category is required for expenses and income."));
   if(category is not null && operation.Kind=="Expense" && category.Type!=CategoryType.Expense) checks.Add(new("CategoryType",1,0,false,"An expense must use an expense category."));
   if(category is not null && operation.Kind=="Income" && category.Type!=CategoryType.Income) checks.Add(new("CategoryType",1,0,false,"Income must use an income category."));
   if(operation.Kind is "DebtCreate" or "Correction") checks.Add(new("UnsupportedOperation",0,1,false,"Operation requires review."));
   foreach(var check in checks) run.ValidationResults.Add(new(){Type=check.Type,ExpectedValue=check.Expected,ActualValue=check.Actual,Difference=check.Actual-check.Expected,Status=check.Passed?ValidationStatus.Pass:ValidationStatus.Fail,Message=check.Message});
   if(checks.Any(x=>!x.Passed)){batch.Status=BatchStatus.NeedsReview;run.Status=RunStatus.Completed;run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await telegram.SendAsync(batch.User.TelegramUserId,$"⚠️ Не удалось подтвердить запись. Распознано: {operation.Amount:0.00} {operation.Currency}. Запись требует проверки.",ct);return;}
   await using var tx=await db.Database.BeginTransactionAsync(ct); var now=DateTimeOffset.UtcNow; var type=Enum.Parse<TransactionType>(operation.Kind); var transaction=new Transaction{CreatedByUserId=batch.UserId,Type=type,TransactionDate=now,CurrencyCode=operation.Currency,Amount=operation.Amount,Description=operation.Description,MerchantName=operation.Merchant,CategoryId=category?.Id,InputBatchId=id,CreatedAt=now,UpdatedAt=now};
   if(operation.AccountId is long accountId)
   {
    var movement=type==TransactionType.BalanceAdjustment ? operation.Amount-(await db.AccountMovements.Where(x=>x.AccountId==accountId).SumAsync(x=>(decimal?)x.Amount,ct)??0) : type is TransactionType.Expense or TransactionType.DebtSettlement ? -operation.Amount : operation.Amount; transaction.Movements.Add(new(){AccountId=accountId,Amount=movement});
   }
   if(type==TransactionType.Transfer && operation.ToAccountId is long toId) transaction.Movements.Add(new(){AccountId=toId,Amount=operation.Amount});
   checks.Add(validator.ValidateMovements(operation,transaction.Movements.Select(x=>x.Amount).ToList())); var movementCheck=checks[^1]; run.ValidationResults.Add(new(){Type=movementCheck.Type,ExpectedValue=movementCheck.Expected,ActualValue=movementCheck.Actual,Difference=movementCheck.Actual-movementCheck.Expected,Status=movementCheck.Passed?ValidationStatus.Pass:ValidationStatus.Fail,Message=movementCheck.Message});
   if(!movementCheck.Passed){batch.Status=BatchStatus.NeedsReview;await db.SaveChangesAsync(ct);await tx.RollbackAsync(ct);return;}
   Receipt? transactionReceipt=null; db.Transactions.Add(transaction); if(operation.Receipt is {} receipt) { transactionReceipt=new(){Transaction=transaction,MerchantName=operation.Merchant??operation.Description,Subtotal=receipt.Subtotal,Tax=receipt.Tax,ServiceCharge=receipt.ServiceCharge,Discount=receipt.Discount,Rounding=receipt.Rounding,Total=receipt.Total,CurrencyCode=operation.Currency,RawExtractedJson=output,Items=receipt.Items.Select(x=>new ReceiptItem{RawName=x.Name,NormalizedName=x.Name,Quantity=x.Quantity,UnitPrice=x.UnitPrice,BaseAmount=x.BaseAmount,Discount=x.Discount,TaxAllocated=x.TaxAllocated,ServiceChargeAllocated=x.ServiceChargeAllocated,FinalAmount=x.FinalAmount,CategoryId=x.CategoryId,AiConfidence=x.Confidence}).ToList()}; db.Receipts.Add(transactionReceipt); }
   batch.Status=BatchStatus.Completed;batch.CompletedAt=now;run.Status=RunStatus.Completed;run.CompletedAt=now;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);logger.LogDebug("Database write committed: TransactionId={TransactionId}, InputBatchId={InputBatchId}, Type={Type}, Amount={Amount}, Currency={Currency}, CategoryId={CategoryId}, Description={Description}, Merchant={Merchant}, AccountMovements={AccountMovements}, ReceiptId={ReceiptId}, AiRunId={AiRunId}",transaction.Id,id,transaction.Type,transaction.Amount,transaction.CurrencyCode,transaction.CategoryId,transaction.Description,transaction.MerchantName,JsonSerializer.Serialize(transaction.Movements.Select(x=>new{x.Id,x.AccountId,x.Amount})),transactionReceipt?.Id,run.Id);var categoryPath=category is null?"Без категории":category.Parent is null?category.Name:$"{category.Parent.Name} → {category.Name}";var operationName=type switch{TransactionType.Expense=>"Расход",TransactionType.Income=>"Доход",TransactionType.Transfer=>"Перевод",TransactionType.BalanceAdjustment=>"Корректировка баланса",TransactionType.DebtSettlement=>"Погашение долга",_=>type.ToString()};var accountName=operation.AccountId is long selectedAccountId?accounts.FirstOrDefault(x=>x.Id==selectedAccountId)?.Name:null;var receiptLines=operation.Receipt is { Items.Count:>0 } receiptDraft?$"\nПозиции:\n{string.Join("\n",receiptDraft.Items.Select(x=>$"• {x.Name}: {x.FinalAmount:0.00} {operation.Currency}"))}":"";await telegram.SendAsync(batch.User.TelegramUserId,$"✅ Транзакция записана\n\nТип: {operationName}\nСумма: {operation.Amount:0.00} {operation.Currency}\nКатегория: {categoryPath}\nОписание: {operation.Merchant??operation.Description}{(accountName is null?"":$"\nСчёт: {accountName}")}{receiptLines}\n\nID операции: {transaction.Id}",ct);
  }
  catch(Exception ex) when(ex is not OperationCanceledException) { batch.Status=BatchStatus.Failed;if(run!=null){run.Status=RunStatus.Failed;run.Error=ex.Message;run.CompletedAt=DateTimeOffset.UtcNow;}await db.SaveChangesAsync(CancellationToken.None); }
 }
}
