using System.Text.Json;
using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.Services;
public sealed class BatchProcessor(IDbContextFactory<FinanceDbContext> factory, ITelegramClient telegram, IVoiceTranscriptionService transcription, IAiExtractionService extraction, IAiAnalyticsService analytics, IAnalyticsQueryExecutor queryExecutor, FinanceOptions options, ILogger<BatchProcessor> logger)
{
 private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
 private static readonly HashSet<string> GenericDescriptions = new(StringComparer.OrdinalIgnoreCase)
 {
  "расход", "трата", "покупка", "expense", "spending", "purchase",
  "доход", "поступление", "income", "transaction", "транзакция", "операция"
 };
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
  if(await db.Transactions.AnyAsync(x=>x.InputBatchId==id,ct) || await db.Debts.AnyAsync(x=>x.InputBatchId==id,ct)){batch.Status=BatchStatus.Completed;batch.CompletedAt??=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);return;}
  if(batch.Status==BatchStatus.Collecting && batch.LastMessageAt>DateTimeOffset.UtcNow.AddSeconds(-options.MessageBatchDelaySeconds)) return;
  var previousStatus=batch.Status;var previousProcessingStartedAt=batch.ProcessingStartedAt;var processingStartedAt=DateTimeOffset.UtcNow;IQueryable<InputBatch> claim=db.InputBatches.Where(x=>x.Id==id&&x.Status==previousStatus);if(previousStatus==BatchStatus.Processing)claim=claim.Where(x=>x.ProcessingStartedAt==previousProcessingStartedAt);var claimed=await claim.ExecuteUpdateAsync(x=>x.SetProperty(y=>y.Status,BatchStatus.Processing).SetProperty(y=>y.ProcessingStartedAt,processingStartedAt),ct);if(claimed==0)return;batch.Status=BatchStatus.Processing;batch.ProcessingStartedAt=processingStartedAt;
  AiRun? run=null; string? confirmation=null;
  try
  {
   var texts=batch.Messages.Where(x=>!string.IsNullOrWhiteSpace(x.Text)).OrderBy(x=>x.CreatedAt).Select(x=>$"[{x.CreatedAt:O}] {x.Text}").ToList(); var images=new List<string>();
   foreach(var message in batch.Messages.Where(x=>x.Type!=InputMessageType.Text && x.TelegramFileId!=null)) { var file=await telegram.DownloadAsync(message.TelegramFileId!,ct); if(message.Type==InputMessageType.Voice){message.Transcript=await transcription.TranscribeAsync(file,ct);texts.Add($"[{message.CreatedAt:O}] {message.Transcript}");} else images.Add($"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}"); }
   var accounts=await db.Accounts.Where(x=>x.IsActive&&(x.OwnerUserId==null||x.OwnerUserId==batch.UserId)).Select(x=>new{x.Id,x.Name,x.CurrencyCode}).ToListAsync(ct); var categories=await db.Categories.Where(x=>x.IsActive).Select(x=>new{x.Id,x.Name,x.ParentId,x.Type}).ToListAsync(ct); var context=JsonSerializer.Serialize(new{accounts,categories,defaultCurrency=options.DefaultCurrency,currentDateTime=DateTimeOffset.UtcNow.ToOffset(MalaysiaOffset),timeZone="Asia/Kuala_Lumpur (UTC+08:00)",databaseSchema="Users(Id,TelegramUserId,Name); Accounts(Id,Name,CurrencyCode,OwnerUserId,IsActive); Categories(Id,Name,ParentId,Type,IsActive); Transactions(Id,CreatedByUserId,Type,TransactionDate,CurrencyCode,Amount,Description,MerchantName,CategoryId); AccountMovements(Id,TransactionId,AccountId,Amount); Receipts(Id,TransactionId,MerchantName,ReceiptDate,Subtotal,Tax,ServiceCharge,Discount,Rounding,Total,CurrencyCode); ReceiptItems(Id,ReceiptId,RawName,NormalizedName,Quantity,UnitPrice,BaseAmount,Discount,TaxAllocated,ServiceChargeAllocated,FinalAmount,CategoryId); Debts(Id,CreatedByUserId,Direction,Counterparty,CurrencyCode,OriginalAmount,Description,CreatedAt,Status); DebtPayments(Id,DebtId,TransactionId,Amount,PaidAt)"});
   run=new AiRun{InputBatchId=id,Model=options.OpenAiModel,InputJson=JsonSerializer.Serialize(new{texts,imageCount=images.Count,context}),StartedAt=DateTimeOffset.UtcNow,Status=RunStatus.Running}; db.AiRuns.Add(run); await db.SaveChangesAsync(ct);
   logger.LogDebug("Database write: AiRunId={AiRunId}, InputBatchId={InputBatchId}, Model={Model}, InputJson={InputJson}, Status={Status}",run.Id,run.InputBatchId,run.Model,run.InputJson,run.Status);
   var aiInput=new AiInput(texts,images,context);
   var plan=await analytics.PlanAsync(aiInput,ct);
   if(plan.IsQuestion)
   {
    if(string.IsNullOrWhiteSpace(plan.Sql)){batch.Status=BatchStatus.NeedsReview;run.Status=RunStatus.Completed;run.OutputJson=JsonSerializer.Serialize(plan);run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await NotifyAsync(batch.User.TelegramUserId,plan.ClarificationQuestion??"Уточните, пожалуйста, какой финансовый показатель и период нужно проанализировать.",ct);return;}
    var results=await queryExecutor.ExecuteAsync(plan.Sql,batch.UserId,ct);
    var answer=await analytics.AnswerAsync(aiInput,plan,results,ct);
    run.OutputJson=JsonSerializer.Serialize(new{plan,results,answer});run.Status=RunStatus.Completed;run.CompletedAt=DateTimeOffset.UtcNow;batch.Status=BatchStatus.Completed;batch.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await NotifyAsync(batch.User.TelegramUserId,answer,ct);return;
   }
   var output=await extraction.ExtractAsync(aiInput,ct); logger.LogDebug("OpenAI extracted operations for InputBatchId={InputBatchId}: {Output}",id,output); run.OutputJson=output;
   var operations=ReadOperations(output); var validator=new ValidationService(options.ValidationRoundingTolerance);
   var prepared=new List<(ExtractedOperation Operation,Category? Category,Account? Account,Account? ToAccount,List<ValidationCheck> Checks)>();
   foreach(var operation in operations)
   {
    var checks=validator.Validate(operation).ToList();
    Category? category=operation.CategoryId is long categoryId ? await db.Categories.Include(x=>x.Parent).SingleOrDefaultAsync(x=>x.Id==categoryId&&x.IsActive,ct) : null;
    if(operation.Kind is "Expense" or "Income") checks.Add(new("Category",1,category is null?0:1,category is not null,$"{operation.Merchant??operation.Description}: уточните категорию."));
    var hasSpecificPurpose=!string.IsNullOrWhiteSpace(operation.Merchant)||(!string.IsNullOrWhiteSpace(operation.Description)&&!GenericDescriptions.Contains(operation.Description.Trim().TrimEnd('.','!','?')));
    if((operation.Kind is "Expense" or "Income")&&!hasSpecificPurpose) checks.Add(new("Purpose",1,0,false,"Уточните назначение операции."));
    if(category is not null&&operation.Kind=="Expense"&&category.Type!=CategoryType.Expense) checks.Add(new("CategoryType",1,0,false,$"{operation.Description}: выбрана категория дохода для расхода."));
    if(category is not null&&operation.Kind=="Income"&&category.Type!=CategoryType.Income) checks.Add(new("CategoryType",1,0,false,$"{operation.Description}: выбрана категория расхода для дохода."));
    Account? account=operation.AccountId is long accountId ? await db.Accounts.SingleOrDefaultAsync(x=>x.Id==accountId&&x.IsActive&&(x.OwnerUserId==null||x.OwnerUserId==batch.UserId),ct) : null;
    if((operation.Kind is "Expense" or "Income" or "BalanceAdjustment" or "DebtSettlement")&&account is null) checks.Add(new("Account",1,0,false,$"{operation.Merchant??operation.Description}: не удалось определить счёт."));
    Account? toAccount=operation.ToAccountId is long toAccountId ? await db.Accounts.SingleOrDefaultAsync(x=>x.Id==toAccountId&&x.IsActive&&(x.OwnerUserId==null||x.OwnerUserId==batch.UserId),ct) : null;
    if(operation.Kind=="Transfer"&&(account is null||toAccount is null)) checks.Add(new("TransferAccounts",2,(account is null?0:1)+(toAccount is null?0:1),false,$"{operation.Description}: нужны исходный и целевой счета."));
    if(!string.IsNullOrWhiteSpace(operation.ClarificationQuestion)) checks.Add(new("Clarification",1,0,false,operation.ClarificationQuestion));
    if(operation.Kind=="DebtCreate") { if(operation.Amount<=0) checks.Add(new("DebtAmount",1,operation.Amount,false,"Уточните положительную сумму долга."));if(string.IsNullOrWhiteSpace(operation.Counterparty))checks.Add(new("DebtCounterparty",1,0,false,"Уточните контрагента долга."));if(!Enum.TryParse<DebtDirection>(operation.DebtDirection,true,out _))checks.Add(new("DebtDirection",1,0,false,"Уточните, кто кому должен.")); }
    if(operation.Kind=="Correction") checks.Add(new("UnsupportedOperation",0,1,false,"Исправление требует уточнения."));
    foreach(var check in checks) run.ValidationResults.Add(new(){Type=check.Type,ExpectedValue=check.Expected,ActualValue=check.Actual,Difference=check.Actual-check.Expected,Status=check.Passed?ValidationStatus.Pass:ValidationStatus.Fail,Message=check.Message});
    prepared.Add((operation,category,account,toAccount,checks));
   }
   var failures=prepared.SelectMany(x=>x.Checks).Where(x=>!x.Passed).Select(x=>x.Message).Distinct().ToList();
   if(failures.Count>0){batch.Status=BatchStatus.NeedsReview;run.Status=RunStatus.Completed;run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await NotifyAsync(batch.User.TelegramUserId,$"⚠️ Пока не могу записать все операции.\n\n{string.Join("\n",failures.Select(x=>$"• {x}"))}\n\nУточните недостающие данные — весь контекст сохранён.",ct);return;}
   await using var tx=await db.Database.BeginTransactionAsync(ct);var now=DateTimeOffset.UtcNow;var saved=new List<(ExtractedOperation Operation,Transaction? Transaction,Debt? Debt,Category? Category,Account? Account)>();
   for(var operationIndex=0;operationIndex<prepared.Count;operationIndex++)
   {
    var item=prepared[operationIndex];
    var operation=item.Operation;var operationDate=operation.TransactionDate??DateTimeOffset.UtcNow.ToOffset(MalaysiaOffset);
    if(operation.Kind=="DebtCreate") { var debt=new Debt{CreatedByUserId=batch.UserId,Direction=Enum.Parse<DebtDirection>(operation.DebtDirection!,true),Counterparty=operation.Counterparty!.Trim(),CurrencyCode=operation.Currency,OriginalAmount=operation.Amount,Description=operation.Description,CreatedAt=operationDate,Status=DebtStatus.Open,InputBatchId=id,InputBatchOperationIndex=operationIndex};db.Debts.Add(debt);saved.Add((operation,null,debt,item.Category,item.Account));continue; }
    var type=Enum.Parse<TransactionType>(operation.Kind);var transaction=new Transaction{CreatedByUserId=batch.UserId,Type=type,TransactionDate=operationDate,CurrencyCode=operation.Currency,Amount=operation.Amount,Description=operation.Description,MerchantName=operation.Merchant,CategoryId=item.Category?.Id,InputBatchId=id,InputBatchOperationIndex=operationIndex,CreatedAt=now,UpdatedAt=now};
    if(item.Account is not null){var movement=type==TransactionType.BalanceAdjustment?operation.Amount-(await db.AccountMovements.Where(x=>x.AccountId==item.Account.Id).SumAsync(x=>(decimal?)x.Amount,ct)??0):type is TransactionType.Expense or TransactionType.DebtSettlement?-operation.Amount:operation.Amount;transaction.Movements.Add(new(){Account=item.Account,Amount=movement});}
    if(type==TransactionType.Transfer&&item.ToAccount is not null)transaction.Movements.Add(new(){Account=item.ToAccount,Amount=operation.Amount});
    var movementCheck=validator.ValidateMovements(operation,transaction.Movements.Select(x=>x.Amount).ToList());run.ValidationResults.Add(new(){Type=movementCheck.Type,ExpectedValue=movementCheck.Expected,ActualValue=movementCheck.Actual,Difference=movementCheck.Actual-movementCheck.Expected,Status=movementCheck.Passed?ValidationStatus.Pass:ValidationStatus.Fail,Message=movementCheck.Message});if(!movementCheck.Passed)throw new InvalidDataException($"Invalid movements for {operation.Description}.");
    db.Transactions.Add(transaction);if(operation.Receipt is{} receipt)db.Receipts.Add(new(){Transaction=transaction,MerchantName=operation.Merchant??operation.Description,ReceiptDate=operationDate,Subtotal=receipt.Subtotal,Tax=receipt.Tax,ServiceCharge=receipt.ServiceCharge,Discount=receipt.Discount,Rounding=receipt.Rounding,Total=receipt.Total,CurrencyCode=operation.Currency,RawExtractedJson=output,Items=receipt.Items.Select(x=>new ReceiptItem{RawName=x.Name,NormalizedName=x.Name,Quantity=x.Quantity,UnitPrice=x.UnitPrice,BaseAmount=x.BaseAmount,Discount=x.Discount,TaxAllocated=x.TaxAllocated,ServiceChargeAllocated=x.ServiceChargeAllocated,FinalAmount=x.FinalAmount,CategoryId=x.CategoryId,AiConfidence=x.Confidence}).ToList()});
    saved.Add((operation,transaction,null,item.Category,item.Account));
   }
   batch.Status=BatchStatus.Completed;batch.CompletedAt=now;run.Status=RunStatus.Completed;run.CompletedAt=now;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
   if(saved.Count==1&&saved[0].Debt is{} singleDebt) confirmation=$"✅ Долг записан\n\n{(singleDebt.Direction==DebtDirection.Payable?"Вы должны":"Вам должен(на)")}: {singleDebt.Counterparty}\nСумма: {singleDebt.OriginalAmount:0.00} {singleDebt.CurrencyCode}\nДата: {singleDebt.CreatedAt:dd.MM.yyyy HH:mm zzz}\nОписание: {singleDebt.Description}\n\nID долга: {singleDebt.Id}";
   else if(saved.Count==1){var single=saved[0];var categoryPath=single.Category is null?"Без категории":single.Category.Parent is null?single.Category.Name:$"{single.Category.Parent.Name} → {single.Category.Name}";confirmation=$"✅ Транзакция записана\n\nСумма: {single.Operation.Amount:0.00} {single.Operation.Currency}\nДата: {single.Transaction!.TransactionDate:dd.MM.yyyy HH:mm zzz}\nКатегория: {categoryPath}\nОписание: {single.Operation.Merchant??single.Operation.Description}{(single.Account is null?"":$"\nСчёт: {single.Account.Name}")}\n\nID операции: {single.Transaction.Id}";}
   else {var lines=saved.Select((x,i)=>x.Debt is not null?$"{i+1}. **Долг {x.Debt.OriginalAmount:0.00} {x.Debt.CurrencyCode}** — {(x.Debt.Direction==DebtDirection.Payable?"вы должны":"вам должен(на)")} {x.Debt.Counterparty} (ID {x.Debt.Id})":$"{i+1}. **{x.Operation.Amount:0.00} {x.Operation.Currency}** — {x.Operation.Merchant??x.Operation.Description}, {x.Category?.Name??"без категории"}{(x.Account is null?"":$", счёт {x.Account.Name}")} (ID {x.Transaction!.Id})");confirmation=$"✅ **Записано операций: {saved.Count}**\n\n{string.Join("\n",lines)}";}
  }
  catch(Exception ex) when(ex is not OperationCanceledException) { logger.LogError(ex,"Failed to process InputBatchId={InputBatchId}",id);foreach(var entry in db.ChangeTracker.Entries().Where(x=>x.State==EntityState.Added&&x.Entity is Transaction or AccountMovement or Receipt or ReceiptItem or Debt or DebtPayment).ToList())entry.State=EntityState.Detached;batch.Status=BatchStatus.Failed;if(run!=null){run.Status=RunStatus.Failed;run.Error=ex.Message;run.CompletedAt=DateTimeOffset.UtcNow;}await db.SaveChangesAsync(CancellationToken.None);await NotifyAsync(batch.User.TelegramUserId,"❌ Не удалось обработать сообщение. Ошибка сохранена в журнале; отправьте его ещё раз или проверьте настройки счетов.",CancellationToken.None);return; }
  if(confirmation is not null) await NotifyAsync(batch.User.TelegramUserId,confirmation,ct);
 }
 private static IReadOnlyList<ExtractedOperation> ReadOperations(string json)
 {
  using var document=JsonDocument.Parse(json);
  if(document.RootElement.TryGetProperty("operations",out _)){var operations=JsonSerializer.Deserialize<ExtractedOperations>(json,JsonOptions)?.Operations??throw new InvalidDataException("Empty AI operations.");return operations.Count is >0 and <=50?operations:throw new InvalidDataException("AI must return between 1 and 50 operations.");}
  return [JsonSerializer.Deserialize<ExtractedOperation>(json,JsonOptions)??throw new InvalidDataException("Empty AI operation.")];
 }
 private async Task NotifyAsync(long chatId,string message,CancellationToken ct) { try { await telegram.SendRichMessageAsync(chatId,message,ct); } catch(Exception ex) when(ex is not OperationCanceledException) { logger.LogError(ex,"Telegram notification failed for ChatId={ChatId}",chatId); } }
}
