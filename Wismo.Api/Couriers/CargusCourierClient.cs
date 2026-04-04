using Microsoft.Extensions.Options;

namespace Wismo.Api.Couriers;

public sealed class CargusCourierClient(
    HttpClient httpClient,
    IOptionsMonitor<CourierIntegrationOptions> options,
    ILogger<CargusCourierClient> logger)
    : CourierStatusClientBase(httpClient, logger)
{
    public override string CourierCode => "CARGUS";
    protected override CourierProviderOptions Options => options.CurrentValue.Cargus;
}
