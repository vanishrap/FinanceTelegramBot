using FinanceBot.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace FinanceBot.Infrastructure.Services;
public sealed class PollingWorker(ITelegramClient telegram, MessageBatchService batches, BatchProcessor processor, ILogger<PollingWorker> logger) : BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken) { long offset=0; while(!stoppingToken.IsCancellationRequested) { try { foreach(var update in await telegram.GetUpdatesAsync(offset,stoppingToken)){logger.LogDebug("Telegram message received: UpdateId={UpdateId}, MessageId={MessageId}, UserId={UserId}, UserName={UserName}, Text={Text}, PhotoFileId={PhotoFileId}, VoiceFileId={VoiceFileId}",update.UpdateId,update.MessageId,update.UserId,update.UserName,update.Text,update.PhotoFileId,update.VoiceFileId);offset=Math.Max(offset,update.UpdateId+1);if(await batches.AddAsync(update,stoppingToken)) await telegram.SendAsync(update.UserId,"📥 Сообщение получено и добавлено в очередь обработки.",stoppingToken);} await processor.ProcessDueAsync(stoppingToken); } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){} catch(Exception ex){logger.LogError(ex,"Polling iteration failed");await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);} } }
}
