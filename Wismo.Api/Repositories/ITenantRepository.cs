using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public interface ITenantRepository
{
    Task<bool> ExistsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByIdAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByIdReadOnlyAsync(int tenantId, CancellationToken cancellationToken = default);
    void Add(Tenant tenant);
}
