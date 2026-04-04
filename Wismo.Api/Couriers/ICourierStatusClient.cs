namespace Wismo.Api.Couriers;

public interface ICourierStatusClient
{
    string CourierCode { get; }
    Task<CourierStatusResult?> GetStatusAsync(string awb, CancellationToken cancellationToken = default);
}
