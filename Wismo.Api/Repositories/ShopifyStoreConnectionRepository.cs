using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class ShopifyStoreConnectionRepository(AppDbContext db) : IShopifyStoreConnectionRepository
{
    // Shopify OAuth callback / webhook entry: tenant is resolved FROM the shop domain, so this read must span tenants.
    public Task<ShopifyStoreConnection?> GetByShopDomainWithTenantAsync(string shopDomain, CancellationToken cancellationToken = default)
        => db.ShopifyStoreConnections
            .IgnoreQueryFilters()
            .Include(item => item.Tenant)
            .FirstOrDefaultAsync(item => item.ShopDomain == shopDomain, cancellationToken);

    // Webhook arrives without a JWT; the tenant is identified by the shop domain on this row.
    public Task<int?> GetTenantIdByActiveShopDomainAsync(string shopDomain, CancellationToken cancellationToken = default)
        => db.ShopifyStoreConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.IsActive && item.ShopDomain == shopDomain)
            .Select(item => (int?)item.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ShopifyStoreConnection connection)
        => db.ShopifyStoreConnections.Add(connection);
}
