namespace Wismo.Api.Features.Shopify;

public sealed class ShopifyOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scopes { get; set; } = "read_orders,write_webhooks,read_shop";
    public string PublicAppUrl { get; set; } = string.Empty;
    public string FrontendUrl { get; set; } = "http://localhost:5173";
    public string ApiVersion { get; set; } = "2025-01";
}

