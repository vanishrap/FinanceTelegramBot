using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Services;
public sealed class MessageBatchService(IDbContextFactory<FinanceDbContext> factory, FinanceOptions options)
{
 public async Task AddAsync(TelegramUpdate update, CancellationToken ct)
 {
  if (!options.AllowedTelegramUserIds.Contains(update.UserId)) return;
  await using var db=await factory.CreateDbContextAsync(ct); if(await db.InputMessages.AnyAsync(x=>x.TelegramMessageId==update.MessageId,ct)) return;
  var now=DateTimeOffset.UtcNow; var user=await db.Users.SingleOrDefaultAsync(x=>x.TelegramUserId==update.UserId,ct) ?? new User{TelegramUserId=update.UserId,Name=update.UserName,CreatedAt=now}; if(user.Id==0) db.Users.Add(user);
  // SQLite cannot translate ordering by DateTimeOffset. Keep the selective part of
  // the query in SQL and order the user's collecting batches in memory.
  var collectingBatches=await db.InputBatches.Where(x=>x.UserId==user.Id && x.Status==BatchStatus.Collecting).ToListAsync(ct);
  var batch=collectingBatches.MaxBy(x=>x.LastMessageAt);
  if(batch is null) { batch=new InputBatch{User=user,StartedAt=now,LastMessageAt=now,Status=BatchStatus.Collecting}; db.InputBatches.Add(batch); } else batch.LastMessageAt=now;
  var message = update.VoiceFileId is not null ? new InputMessage { TelegramMessageId=update.MessageId, Type=InputMessageType.Voice, Text=update.Text, TelegramFileId=update.VoiceFileId, CreatedAt=now } : update.PhotoFileId is not null ? new InputMessage { TelegramMessageId=update.MessageId, Type=InputMessageType.Photo, Text=update.Text, TelegramFileId=update.PhotoFileId, CreatedAt=now } : new InputMessage { TelegramMessageId=update.MessageId, Type=InputMessageType.Text, Text=update.Text, CreatedAt=now };
  batch.Messages.Add(message);
  await db.SaveChangesAsync(ct);
 }
}
