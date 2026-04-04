using Microsoft.Extensions.Options;
using Wismo.Api.Models;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;

namespace Wismo.Api.Couriers;

public interface IAwbStatusSyncService
{
    Task<int> SyncInTransitStatusesAsync(CancellationToken cancellationToken = default);
}

public sealed class AwbStatusSyncService(
    ISupportTicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    IEnumerable<ICourierStatusClient> courierClients,
    IOptionsMonitor<AwbTrackingOptions> options,
    ILogger<AwbStatusSyncService> logger,
    ITenantNotificationService tenantNotificationService) : IAwbStatusSyncService
{
    private readonly IOptionsMonitor<AwbTrackingOptions> _options = options;
    private readonly ILogger<AwbStatusSyncService> _logger = logger;
    private readonly ITenantNotificationService _tenantNotificationService = tenantNotificationService;
    private readonly Dictionary<string, ICourierStatusClient> _clientsByCode = courierClients
        .GroupBy(client => client.CourierCode, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<int> SyncInTransitStatusesAsync(CancellationToken cancellationToken = default)
    {
        var settings = _options.CurrentValue;
        var inTransitStatuses = (settings.InTransitStatuses ?? new List<string>())
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inTransitStatuses.Length == 0)
        {
            _logger.LogWarning("AWB sync skipped because AwbTracking:InTransitStatuses is empty.");
            return 0;
        }

        var ticketsInTransit = await ticketRepository.GetByStatusesIgnoringFiltersAsync(inTransitStatuses, cancellationToken);

        if (ticketsInTransit.Count == 0)
        {
            _logger.LogDebug("AWB sync found no tickets in the configured in-transit statuses.");
            return 0;
        }

        var maxParallelRequests = Math.Clamp(settings.MaxParallelRequests, 1, 20);
        using var semaphore = new SemaphoreSlim(maxParallelRequests, maxParallelRequests);

        _logger.LogInformation(
            "AWB sync started. TicketsInTransit={TicketsInTransit}, MaxParallelRequests={MaxParallelRequests}.",
            ticketsInTransit.Count,
            maxParallelRequests);

        var updateTasks = ticketsInTransit
            .Select(ticket => FetchUpdatedStatusAsync(ticket, semaphore, cancellationToken))
            .ToArray();

        StatusUpdateResult[] updates;

        try
        {
            updates = await Task.WhenAll(updateTasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AWB sync canceled while waiting for courier responses.");
            throw;
        }

        var updatedCount = 0;
        var updatedTenantIds = new HashSet<int>();
        var unchangedCount = 0;
        var noStatusCount = 0;
        var invalidAwbCount = 0;
        var unknownCourierCount = 0;
        var failedCount = 0;

        foreach (var update in updates)
        {
            switch (update.Outcome)
            {
                case TicketSyncOutcome.Fetched:
                    if (string.Equals(update.Ticket.Status, update.NewStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        unchangedCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(update.NewStatus))
                    {
                        noStatusCount++;
                        continue;
                    }

                    update.Ticket.Status = update.NewStatus;
                    updatedCount++;
                    updatedTenantIds.Add(update.Ticket.TenantId);
                    break;

                case TicketSyncOutcome.NoStatus:
                    noStatusCount++;
                    break;

                case TicketSyncOutcome.InvalidAwb:
                    invalidAwbCount++;
                    break;

                case TicketSyncOutcome.UnknownCourier:
                    unknownCourierCount++;
                    break;

                case TicketSyncOutcome.Failed:
                    failedCount++;
                    break;
            }
        }

        if (updatedCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await _tenantNotificationService.NotifyTenantsDashboardUpdatedAsync(updatedTenantIds, "awb-status-sync", cancellationToken);
        }
        _logger.LogInformation(
            "AWB sync finished. Processed={ProcessedCount}, Updated={UpdatedCount}, Unchanged={UnchangedCount}, NoStatus={NoStatusCount}, InvalidAwb={InvalidAwbCount}, UnknownCourier={UnknownCourierCount}, Failed={FailedCount}.",
            updates.Length,
            updatedCount,
            unchangedCount,
            noStatusCount,
            invalidAwbCount,
            unknownCourierCount,
            failedCount);

        if (failedCount > 0)
        {
            _logger.LogWarning("AWB sync completed with courier failures. FailedCount={FailedCount}.", failedCount);
        }

        return updatedCount;
    }

    private async Task<StatusUpdateResult> FetchUpdatedStatusAsync(
        SupportTicket ticket,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        if (!AwbReferenceParser.TryParse(ticket.OrderNumber, out var courierCode, out var awb))
        {
            _logger.LogDebug("Skip AWB sync for ticket {TicketId}. OrderNumber format is not <COURIER>:<AWB>.", ticket.Id);
            return new StatusUpdateResult(ticket, null, TicketSyncOutcome.InvalidAwb);
        }

        if (!_clientsByCode.TryGetValue(courierCode, out var courierClient))
        {
            _logger.LogWarning("No courier client registered for courier code {CourierCode}.", courierCode);
            return new StatusUpdateResult(ticket, null, TicketSyncOutcome.UnknownCourier);
        }

        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var courierStatus = await courierClient.GetStatusAsync(awb, cancellationToken);

            if (courierStatus is null || string.IsNullOrWhiteSpace(courierStatus.ExternalStatus))
            {
                return new StatusUpdateResult(ticket, null, TicketSyncOutcome.NoStatus);
            }

            var mappedStatus = AwbStatusMapper.MapToInternalStatus(courierStatus.ExternalStatus);
            return new StatusUpdateResult(ticket, mappedStatus, TicketSyncOutcome.Fetched);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected AWB sync failure for ticket {TicketId}.", ticket.Id);
            return new StatusUpdateResult(ticket, null, TicketSyncOutcome.Failed);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private enum TicketSyncOutcome
    {
        Fetched,
        NoStatus,
        InvalidAwb,
        UnknownCourier,
        Failed
    }

    private sealed record StatusUpdateResult(SupportTicket Ticket, string? NewStatus, TicketSyncOutcome Outcome);
}
