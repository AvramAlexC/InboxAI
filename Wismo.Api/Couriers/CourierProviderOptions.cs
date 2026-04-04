namespace Wismo.Api.Couriers;

public sealed class CourierProviderOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string StatusPath { get; set; } = "/api/awb/{awb}/status";
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
    public string ApiKey { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxRetries { get; set; } = 2;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
