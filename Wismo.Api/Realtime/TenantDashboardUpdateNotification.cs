namespace Wismo.Api.Realtime;

public sealed record TenantDashboardUpdateNotification(string Reason, DateTimeOffset OccurredAtUtc);
