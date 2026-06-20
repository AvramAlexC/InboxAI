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
                t.OrderStatus.ToString(),
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

    // Webhook arrives without a JWT; the tenant is already resolved from the shop domain, so the
    // lookup must opt out of the tenant query filter. Tracked (no AsNoTracking) so the caller can mutate.
    public Task<SupportTicket?> FindIgnoringFiltersAsync(int tenantId, string orderNumber, string intent, CancellationToken cancellationToken = default)
        => db.SupportTickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                ticket => ticket.TenantId == tenantId &&
                          ticket.OrderNumber == orderNumber &&
                          ticket.Intent == intent,
                cancellationToken);

    // orders/delete payloads carry only the numeric Shopify order id, never name/order_number, so the
    // delete handler must match on ShopifyOrderId. Same tenant-filter opt-out and tracking as the lookup above.
    public Task<SupportTicket?> FindByShopifyOrderIdIgnoringFiltersAsync(int tenantId, string shopifyOrderId, string intent, CancellationToken cancellationToken = default)
        => db.SupportTickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                ticket => ticket.TenantId == tenantId &&
                          ticket.ShopifyOrderId == shopifyOrderId &&
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
