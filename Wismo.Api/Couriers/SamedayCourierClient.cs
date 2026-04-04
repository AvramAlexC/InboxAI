using Microsoft.Extensions.Options;

namespace Wismo.Api.Couriers;

public sealed class SamedayCourierClient(
    HttpClient httpClient,
    IOptionsMonitor<CourierIntegrationOptions> options,
    ILogger<SamedayCourierClient> logger)
    : CourierStatusClientBase(httpClient, logger)
{
    public override string CourierCode => "SAMEDAY";
    protected override CourierProviderOptions Options => options.CurrentValue.Sameday;
}
