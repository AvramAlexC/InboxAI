namespace Wismo.Api.Couriers;

public sealed class CourierIntegrationOptions
{
    public CourierProviderOptions Sameday { get; set; } = new();
    public CourierProviderOptions FanCourier { get; set; } = new();
    public CourierProviderOptions Cargus { get; set; } = new();
}
