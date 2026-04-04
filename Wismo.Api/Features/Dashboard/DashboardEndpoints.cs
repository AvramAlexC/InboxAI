using Wismo.Api.Repositories;

namespace Wismo.Api.Features.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalTickets,
    int InTransit,
    int RequiresApproval,
    int Delivered,
    int DeliveryIssue,
    int Other);

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        group.MapGet("/summary", async (ISupportTicketRepository ticketRepository) =>
        {
            var grouped = await ticketRepository.GetStatusCountsAsync();

            var total = grouped.Sum(item => item.Count);
            var inTransit = CountFor(grouped, "InTransit", "In Transit", "In tranzit");
            var requiresApproval = CountFor(grouped, "RequiresApproval");
            var delivered = CountFor(grouped, "Delivered");
            var deliveryIssue = CountFor(grouped, "DeliveryIssue", "Delivery Issue");
            var knownTotal = inTransit + requiresApproval + delivered + deliveryIssue;
            var other = Math.Max(0, total - knownTotal);

            var summary = new DashboardSummaryDto(
                TotalTickets: total,
                InTransit: inTransit,
                RequiresApproval: requiresApproval,
                Delivered: delivered,
                DeliveryIssue: deliveryIssue,
                Other: other);

            return Results.Ok(summary);
        });
    }

    private static int CountFor(IEnumerable<StatusCount> grouped, params string[] statuses)
    {
        var normalized = statuses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return grouped
            .Where(item => normalized.Contains(item.Status))
            .Sum(item => item.Count);
    }
}
