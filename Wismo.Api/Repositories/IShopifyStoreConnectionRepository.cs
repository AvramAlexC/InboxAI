using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public interface IShopifyStoreConnectionRepository
{
    Task<ShopifyStoreConnection?> GetByShopDomainWithTenantAsync(string shopDomain, CancellationToken cancellationToken = default);
    Task<int?> GetTenantIdByActiveShopDomainAsync(string shopDomain, CancellationToken cancellationToken = default);
    void Add(ShopifyStoreConnection connection);
}
