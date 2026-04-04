namespace Wismo.Api.Couriers;

public static class AwbStatusMapper
{
    public static string MapToInternalStatus(string externalStatus)
    {
        if (string.IsNullOrWhiteSpace(externalStatus))
        {
            return "InTransit";
        }

        var normalized = externalStatus.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "delivered", "livrat", "predat"))
        {
            return "Delivered";
        }

        if (ContainsAny(normalized, "returned", "retur", "returnat"))
        {
            return "Returned";
        }

        if (ContainsAny(normalized, "failed", "exception", "esuat", "nereusit", "anulat", "cancel"))
        {
            return "DeliveryIssue";
        }

        return "InTransit";
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
