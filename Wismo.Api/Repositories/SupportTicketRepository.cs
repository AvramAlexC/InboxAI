using Microsoft.EntityFrameworkCore;
using Wismo.Api.DTOs;
using Wismo.Api.Models;

namespace Wismo.Api.Repositories;

public sealed class SupportTicketRepository(AppDbContext db) : ISupportTicketRepository
{
    public Task<List<TicketResponseDto>> GetAllWithTenantAsync(CancellationToken cancellationToken = default)
        => db.SupportTickets
            .Include(t => t.Tenant)
            .Select(t => new TicketResponseDto(
                t.Id,
                t.CustomerEmail,
                t.OrderNumber,
                t.Status,
                t.Tenant == null ? "N/A" : t.Tenant.Name))
            .ToListAsync(cancellationToken);

    public Task<List<SupportTicket>> GetByStatusesIgnoringFiltersAsync(string[] statuses, CancellationToken cancellationToken = default)
        => db.SupportTickets
            .IgnoreQueryFilters()
            .Where(ticket => statuses.Contains(ticket.Status))
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsIgnoringFiltersAsync(int tenantId, string orderNumber, string intent, CancellationToken cancellationToken = default)
        => db.SupportTickets
            .IgnoreQueryFilters()
            .AnyAsync(
                ticket => ticket.TenantId == tenantId &&
                          ticket.OrderNumber == orderNumber &&
                          ticket.Intent == intent,
                cancellationToken);

    public Task<List<StatusCount>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
        => db.SupportTickets
            .GroupBy(ticket => ticket.Status)
            .Select(group => new StatusCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

    public void Add(SupportTicket ticket)
        => db.SupportTickets.Add(ticket);
}
