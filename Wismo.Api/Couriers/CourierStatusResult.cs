namespace Wismo.Api.Couriers;

public sealed record CourierStatusResult(string ExternalStatus, DateTimeOffset? EventTime = null);
