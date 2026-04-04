namespace Wismo.Api.Couriers;

public sealed class AwbTrackingOptions
{
    public string Cron { get; set; } = "0 */10 * * * ?";
    public int MaxParallelRequests { get; set; } = 5;
    public List<string> InTransitStatuses { get; set; } = ["InTransit", "In Transit", "In tranzit"];
}
