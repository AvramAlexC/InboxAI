using Wismo.Api.DTOs;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public interface ISupportTicketRepository
{
    Task<List<TicketResponseDto>> GetAllWithTenantAsync(CancellationToken cancellationToken = default);
    Task<List<SupportTicket>> GetByStatusesIgnoringFiltersAsync(string[] statuses, CancellationToken cancellationToken = default);
    Task<bool> ExistsIgnoringFiltersAsync(int tenantId, string orderNumber, string intent, CancellationToken cancellationToken = default);
    Task<List<StatusCount>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
    void Add(SupportTicket ticket);
}

public sealed record StatusCount(string Status, int Count);
