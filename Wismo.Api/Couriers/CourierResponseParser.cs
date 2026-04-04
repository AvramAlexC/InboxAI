using System.Text.Json;

namespace Wismo.Api.Couriers;

public static class CourierResponseParser
{
    private static readonly string[] StatusKeys =
    [
        "status",
        "statusName",
        "currentStatus",
        "shipmentStatus",
        "state",
        "description"
    ];

    private static readonly string[] EventTimeKeys =
    [
        "eventTime",
        "timestamp",
        "updatedAt",
        "date",
        "statusDate"
    ];

    public static CourierStatusResult? Parse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        using var document = JsonDocument.Parse(responseContent);
        var status = FindFirstStringValue(document.RootElement, StatusKeys);

        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var eventTimeRaw = FindFirstStringValue(document.RootElement, EventTimeKeys);
        DateTimeOffset? eventTime = null;

        if (!string.IsNullOrWhiteSpace(eventTimeRaw) && DateTimeOffset.TryParse(eventTimeRaw, out var parsedDate))
        {
            eventTime = parsedDate;
        }

        return new CourierStatusResult(status, eventTime);
    }

    private static string? FindFirstStringValue(JsonElement element, IEnumerable<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    return property.Value.ToString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nestedResult = FindFirstStringValue(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nestedResult))
                {
                    return nestedResult;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedResult = FindFirstStringValue(item, keys);
                if (!string.IsNullOrWhiteSpace(nestedResult))
                {
                    return nestedResult;
                }
            }
        }

        return null;
    }
}
