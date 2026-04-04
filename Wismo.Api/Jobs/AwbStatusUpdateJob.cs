using Quartz;
using Wismo.Api.Couriers;

namespace Wismo.Api.Jobs;

[DisallowConcurrentExecution]
public sealed class AwbStatusUpdateJob(
    IAwbStatusSyncService awbStatusSyncService,
    ILogger<AwbStatusUpdateJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            logger.LogInformation(
                "AWB sync job started. FireTimeUtc={FireTimeUtc}, ScheduledFireTimeUtc={ScheduledFireTimeUtc}, NextFireTimeUtc={NextFireTimeUtc}.",
                context.FireTimeUtc,
                context.ScheduledFireTimeUtc,
                context.NextFireTimeUtc);

            var updatedCount = await awbStatusSyncService.SyncInTransitStatusesAsync(context.CancellationToken);
            var elapsed = DateTimeOffset.UtcNow - startedAtUtc;

            logger.LogInformation(
                "AWB sync job completed successfully in {ElapsedMs} ms. Updated {UpdatedCount} records.",
                elapsed.TotalMilliseconds,
                updatedCount);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AWB sync job canceled by Quartz.");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "AWB sync job failed. FireTimeUtc={FireTimeUtc}, NextFireTimeUtc={NextFireTimeUtc}.",
                context.FireTimeUtc,
                context.NextFireTimeUtc);

            throw new JobExecutionException(exception, refireImmediately: false);
        }
    }
}
