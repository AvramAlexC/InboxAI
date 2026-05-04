using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class StoreUserRepository(AppDbContext db) : IStoreUserRepository
{
    // Login lookup runs before a tenant context exists — the user's tenant is derived from this row.
    public Task<StoreUser?> GetActiveByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);

    // Signup/duplicate-check lookup; email is unique across all tenants and runs pre-tenant-context.
    public Task<StoreUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

    // First-run bootstrap check; no tenant context available.
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => db.StoreUsers.IgnoreQueryFilters().AnyAsync(cancellationToken);

    // Signup duplicate-email guard; email uniqueness is global, so the check must span tenants.
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.IgnoreQueryFilters().AnyAsync(x => x.Email == email, cancellationToken);

    public void Add(StoreUser user)
        => db.StoreUsers.Add(user);
}
