using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class StoreUserRepository(AppDbContext db) : IStoreUserRepository
{
    public Task<StoreUser?> GetActiveByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);

    public Task<StoreUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => db.StoreUsers.AnyAsync(cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => db.StoreUsers.AnyAsync(x => x.Email == email, cancellationToken);

    public void Add(StoreUser user)
        => db.StoreUsers.Add(user);
}
