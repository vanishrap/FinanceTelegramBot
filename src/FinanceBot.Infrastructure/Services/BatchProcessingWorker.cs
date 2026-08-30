using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.Services;

public sealed class BatchProcessingWorker(BatchProcessor processor, ILogger<BatchProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                await processor.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Batch processing iteration failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
