using Microsoft.Extensions.Options;

namespace Wismo.Api.Couriers;

public sealed class FanCourierClient(
    HttpClient httpClient,
    IOptionsMonitor<CourierIntegrationOptions> options,
    ILogger<FanCourierClient> logger)
    : CourierStatusClientBase(httpClient, logger)
{
    public override string CourierCode => "FAN";
    protected override CourierProviderOptions Options => options.CurrentValue.FanCourier;
}
