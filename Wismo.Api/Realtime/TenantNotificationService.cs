using Microsoft.AspNetCore.SignalR;

namespace Wismo.Api.Realtime;

public interface ITenantNotificationService
{
    Task NotifyTenantDashboardUpdatedAsync(int tenantId, string reason, CancellationToken cancellationToken = default);
    Task NotifyTenantsDashboardUpdatedAsync(IEnumerable<int> tenantIds, string reason, CancellationToken cancellationToken = default);
}

public sealed class TenantNotificationService(IHubContext<TenantDashboardHub, ITenantDashboardClient> hubContext) : ITenantNotificationService
{
    private readonly IHubContext<TenantDashboardHub, ITenantDashboardClient> _hubContext = hubContext;

    public Task NotifyTenantDashboardUpdatedAsync(int tenantId, string reason, CancellationToken cancellationToken = default)
        => NotifyTenantsDashboardUpdatedAsync([tenantId], reason, cancellationToken);

    public async Task NotifyTenantsDashboardUpdatedAsync(IEnumerable<int> tenantIds, string reason, CancellationToken cancellationToken = default)
    {
        var distinctTenantIds = tenantIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (distinctTenantIds.Length == 0)
        {
            return;
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "data-changed" : reason.Trim();
        var payload = new TenantDashboardUpdateNotification(normalizedReason, DateTimeOffset.UtcNow);

        var notifyTasks = distinctTenantIds
            .Select(id => _hubContext.Clients.Group(TenantDashboardHub.GetTenantGroupName(id)).TenantDashboardUpdated(payload));

        await Task.WhenAll(notifyTasks).WaitAsync(cancellationToken);
    }
}
