using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class ShopifyStoreConnectionRepository(AppDbContext db) : IShopifyStoreConnectionRepository
{
    public Task<ShopifyStoreConnection?> GetByShopDomainWithTenantAsync(string shopDomain, CancellationToken cancellationToken = default)
        => db.ShopifyStoreConnections
            .Include(item => item.Tenant)
            .FirstOrDefaultAsync(item => item.ShopDomain == shopDomain, cancellationToken);

    public Task<int?> GetTenantIdByActiveShopDomainAsync(string shopDomain, CancellationToken cancellationToken = default)
        => db.ShopifyStoreConnections
            .AsNoTracking()
            .Where(item => item.IsActive && item.ShopDomain == shopDomain)
            .Select(item => (int?)item.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ShopifyStoreConnection connection)
        => db.ShopifyStoreConnections.Add(connection);
}
