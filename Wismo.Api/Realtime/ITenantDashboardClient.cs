namespace Wismo.Api.Realtime;

public interface ITenantDashboardClient
{
    Task TenantDashboardUpdated(TenantDashboardUpdateNotification notification);
}
