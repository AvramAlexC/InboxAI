using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wismo.Api.Models;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;

namespace Wismo.Api.Features.Shopify;

public sealed class ShopifyWebhookOptions
{
    public string SharedSecret { get; set; } = string.Empty;
    public int? DefaultTenantId { get; set; }
    public Dictionary<string, int> ShopDomainTenantMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ShopifyWebhookEndpoints
{
    private const string OrderCreatedIntent = "SHOPIFY_ORDER_CREATED";

    public static void MapShopifyWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks/shopify")
            .WithTags("Shopify Webhooks");

        group.MapPost("/orders/create", HandleOrderCreatedWebhook);
    }

    private static async Task<IResult> HandleOrderCreatedWebhook(
        HttpRequest request,
        ITenantRepository tenantRepository,
        ISupportTicketRepository ticketRepository,
        IShopifyStoreConnectionRepository connectionRepository,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<ShopifyWebhookOptions> optionsMonitor,
        ILoggerFactory loggerFactory,
        ITenantNotificationService tenantNotificationService,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ShopifyWebhook");

        request.EnableBuffering();

        string payload;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Results.BadRequest(new { Message = "Webhook payload gol." });
        }

        var options = optionsMonitor.CurrentValue;
        var shopDomain = request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();
        var providedHmac = request.Headers["X-Shopify-Hmac-Sha256"].FirstOrDefault();

        if (!IsShopifySignatureValid(payload, providedHmac, options.SharedSecret, logger))
        {
            logger.LogWarning("Shopify webhook signature invalid. ShopDomain={ShopDomain}", shopDomain);
            return Results.Unauthorized();
        }

        var tenantId = await ResolveTenantIdAsync(shopDomain, options, connectionRepository, cancellationToken);
        if (!tenantId.HasValue)
        {
            logger.LogWarning("Unable to resolve tenant for Shopify webhook. ShopDomain={ShopDomain}", shopDomain);
            return Results.BadRequest(new { Message = "Tenantul nu poate fi determinat pentru acest shop Shopify." });
        }

        var tenant = await tenantRepository.GetByIdReadOnlyAsync(tenantId.Value, cancellationToken);

        if (tenant is null)
        {
            return Results.BadRequest(new { Message = "Tenantul configurat pentru webhook nu exista." });
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid Shopify webhook JSON payload. ShopDomain={ShopDomain}", shopDomain);
            return Results.BadRequest(new { Message = "Payload JSON invalid." });
        }

        using (document)
        {
            var root = document.RootElement;

            var orderIdentifier = FirstNonEmpty(
                TryGetString(root, "name"),
                TryGetString(root, "order_number"),
                TryGetString(root, "id"));

            if (string.IsNullOrWhiteSpace(orderIdentifier))
            {
                return Results.BadRequest(new { Message = "Payload-ul nu contine un identificator de comanda." });
            }

            var normalizedOrderNumber = orderIdentifier.StartsWith("SHOPIFY:", StringComparison.OrdinalIgnoreCase)
                ? orderIdentifier
                : $"SHOPIFY:{orderIdentifier}";

            var alreadyIngested = await ticketRepository.ExistsIgnoringFiltersAsync(
                tenant.Id, normalizedOrderNumber, OrderCreatedIntent, cancellationToken);

            if (alreadyIngested)
            {
                logger.LogInformation("Shopify order already ingested. TenantId={TenantId}, OrderNumber={OrderNumber}", tenant.Id, normalizedOrderNumber);
                return Results.Ok(new { Message = "Comanda a fost deja procesata.", OrderNumber = normalizedOrderNumber });
            }

            var customerEmail = FirstNonEmpty(
                TryGetString(root, "contact_email"),
                TryGetString(root, "email"),
                TryGetString(root, "customer", "email"),
                tenant.ContactEmail,
                "unknown@shopify.local");

            var createdAtRaw = TryGetString(root, "created_at");
            var receivedAtUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(createdAtRaw) && DateTimeOffset.TryParse(createdAtRaw, out var createdAt))
            {
                receivedAtUtc = createdAt.UtcDateTime;
            }

            var orderId = FirstNonEmpty(TryGetString(root, "id"), orderIdentifier);
            var messageBody = $"Shopify order/create webhook: shop={shopDomain ?? "unknown"}, orderId={orderId}.";

            var ticket = new SupportTicket
            {
                CustomerEmail = customerEmail,
                OrderNumber = normalizedOrderNumber,
                Status = "New",
                ReceivedAt = receivedAtUtc,
                MessageBody = messageBody,
                Intent = OrderCreatedIntent,
                DraftResponse = null,
                TenantId = tenant.Id
            };

            ticketRepository.Add(ticket);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await tenantNotificationService.NotifyTenantDashboardUpdatedAsync(ticket.TenantId, "shopify-order-created", cancellationToken);

            logger.LogInformation(
                "Shopify order ingested successfully. TicketId={TicketId}, TenantId={TenantId}, OrderNumber={OrderNumber}",
                ticket.Id,
                ticket.TenantId,
                ticket.OrderNumber);

            return Results.Ok(new { TicketId = ticket.Id, ticket.OrderNumber, TenantId = ticket.TenantId });
        }
    }

    private static async Task<int?> ResolveTenantIdAsync(
        string? shopDomain,
        ShopifyWebhookOptions options,
        IShopifyStoreConnectionRepository connectionRepository,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(shopDomain))
        {
            var normalized = shopDomain.Trim().ToLowerInvariant();

            var fromConnection = await connectionRepository.GetTenantIdByActiveShopDomainAsync(normalized, cancellationToken);

            if (fromConnection.HasValue && fromConnection.Value > 0)
            {
                return fromConnection.Value;
            }

            if (options.ShopDomainTenantMap.TryGetValue(shopDomain.Trim(), out var mappedTenantId) && mappedTenantId > 0)
            {
                return mappedTenantId;
            }
        }

        if (options.DefaultTenantId.HasValue && options.DefaultTenantId.Value > 0)
        {
            return options.DefaultTenantId.Value;
        }

        return null;
    }

    private static bool IsShopifySignatureValid(string payload, string? providedHmac, string sharedSecret, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            logger.LogWarning("Shopify:Webhook:SharedSecret is not configured. HMAC validation is skipped.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(providedHmac))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHmac = Convert.ToBase64String(computedBytes);

        var providedBytes = Encoding.UTF8.GetBytes(providedHmac.Trim());
        var computedCompareBytes = Encoding.UTF8.GetBytes(computedHmac);

        return providedBytes.Length == computedCompareBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, computedCompareBytes);
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => current.ToString()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
