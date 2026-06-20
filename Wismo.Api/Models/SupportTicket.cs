namespace Wismo.Api.Models;

public class SupportTicket
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;

    // Numeric Shopify order id (e.g. "450789469"). Persisted so webhooks that only carry `id`
    // (notably orders/delete) can match tickets, since OrderNumber is derived from `name`/`order_number`.
    public string? ShopifyOrderId { get; set; }
    public string Status { get; set; } = "New";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string MessageBody { get; set; } = string.Empty;
    public string Intent { get; set; } = "UNKNOWN";
    public string? DraftResponse { get; set; }
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Open;

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}