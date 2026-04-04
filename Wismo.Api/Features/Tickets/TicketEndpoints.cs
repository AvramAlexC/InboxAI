using MediatR;
using Wismo.Api.Multitenancy;
using Wismo.Api.Repositories;
using WismoAI.Core.Services;

namespace WismoAI.Api.Features.Tickets;

public sealed record ClassifyPreviewRequest(string? CustomerEmail, string MessageBody);

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets")
            .WithTags("Tickets")
            .RequireAuthorization();

        group.MapGet("/", async (ISupportTicketRepository ticketRepository, ITenantContext tenantContext) =>
        {
            if (!tenantContext.TenantId.HasValue)
            {
                return Results.Unauthorized();
            }

            var tickets = await ticketRepository.GetAllWithTenantAsync();
            return Results.Ok(tickets);
        });

        group.MapPost("/", async (CreateTicketCommand command, ISender sender) =>
        {
            var ticketId = await sender.Send(command);
            return Results.Created($"/api/tickets/{ticketId}", new { Id = ticketId });
        });

        group.MapPost("/classify-preview", async (
            ClassifyPreviewRequest request,
            ITicketAiProcessor aiProcessor,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MessageBody))
            {
                return Results.BadRequest(new { Message = "MessageBody este obligatoriu." });
            }

            var classification = await aiProcessor.ProcessTicketAsync(
                new TicketRequest(request.CustomerEmail ?? string.Empty, request.MessageBody),
                cancellationToken);

            return Results.Ok(new
            {
                classification.Intent,
                classification.OrderId,
                classification.ExtractedEmail,
                classification.DraftResponse,
                classification.Confidence
            });
        });
    }
}
