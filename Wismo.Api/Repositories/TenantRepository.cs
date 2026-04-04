using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class TenantRepository(AppDbContext db) : ITenantRepository
{
    public Task<bool> ExistsAsync(int tenantId, CancellationToken cancellationToken = default)
        => db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken);

    public Task<Tenant?> GetByIdAsync(int tenantId, CancellationToken cancellationToken = default)
        => db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public Task<Tenant?> GetByIdReadOnlyAsync(int tenantId, CancellationToken cancellationToken = default)
        => db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public void Add(Tenant tenant)
        => db.Tenants.Add(tenant);
}
