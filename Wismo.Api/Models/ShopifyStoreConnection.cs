namespace Wismo.Api.Models;

public class ShopifyStoreConnection
{
    public int Id { get; set; }
    public string ShopDomain { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
