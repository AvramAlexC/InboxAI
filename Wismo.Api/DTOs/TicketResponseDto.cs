namespace Wismo.Api.DTOs;

public record TicketResponseDto(
    int Id,
    string CustomerEmail,
    string OrderNumber,
    string Status,
    string TenantName
);