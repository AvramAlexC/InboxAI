namespace Wismo.Api.Models;

public class SupportTicket
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string MessageBody { get; set; } = string.Empty;
    public string Intent { get; set; } = "UNKNOWN";
    public string? DraftResponse { get; set; }

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}