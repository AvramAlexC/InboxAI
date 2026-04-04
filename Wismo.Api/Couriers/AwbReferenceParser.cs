namespace Wismo.Api.Couriers;

public static class AwbReferenceParser
{
    public static bool TryParse(string reference, out string courierCode, out string awb)
    {
        courierCode = string.Empty;
        awb = string.Empty;

        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var trimmed = reference.Trim();

        var separatorIndex = trimmed.IndexOfAny([':', '|']);
        if (separatorIndex > 0)
        {
            var courierRaw = trimmed[..separatorIndex].Trim();
            var awbRaw = trimmed[(separatorIndex + 1)..].Trim();

            if (TryNormalizeCourier(courierRaw, out courierCode) && !string.IsNullOrWhiteSpace(awbRaw))
            {
                awb = awbRaw;
                return true;
            }
        }

        var dashParts = trimmed.Split('-', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (dashParts.Length == 2 && TryNormalizeCourier(dashParts[0], out courierCode))
        {
            awb = dashParts[1];
            return !string.IsNullOrWhiteSpace(awb);
        }

        return false;
    }

    private static bool TryNormalizeCourier(string rawCourier, out string courierCode)
    {
        var normalized = rawCourier.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);

        if (normalized.Equals("sameday", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("smd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("smdy", StringComparison.OrdinalIgnoreCase))
        {
            courierCode = "SAMEDAY";
            return true;
        }

        if (normalized.Equals("fan", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("fancourier", StringComparison.OrdinalIgnoreCase))
        {
            courierCode = "FAN";
            return true;
        }

        if (normalized.Equals("cargus", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("urgentcargus", StringComparison.OrdinalIgnoreCase))
        {
            courierCode = "CARGUS";
            return true;
        }

        courierCode = string.Empty;
        return false;
    }
}
