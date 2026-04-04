using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public interface IStoreUserRepository
{
    Task<StoreUser?> GetActiveByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<StoreUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    void Add(StoreUser user);
}
