using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Wismo.Api.Features.Shopify;

public enum ShopifyWebhookVerificationResult
{
    Valid,
    MissingHeader,
    Invalid,
    NotConfigured
}

public interface IShopifyWebhookVerifier
{
    /// <summary>
    /// Verifies a Shopify webhook HMAC-SHA256 signature against the raw request payload.
    /// The provided header value is trimmed before comparison (matching the prior inline
    /// implementation) to tolerate accidental whitespace from proxies/CDNs. Comparison is
    /// case-sensitive because Shopify emits canonical base64 — we do not lowercase the
    /// header. Returns <see cref="ShopifyWebhookVerificationResult.NotConfigured"/> when
    /// the shared secret is missing so callers can hard-fail instead of silently passing.
    /// </summary>
    ShopifyWebhookVerificationResult Verify(string payload, string? providedHmacHeader);
}

public sealed class ShopifyWebhookVerifier : IShopifyWebhookVerifier
{
    private readonly IOptionsMonitor<ShopifyWebhookOptions> _optionsMonitor;

    public ShopifyWebhookVerifier(IOptionsMonitor<ShopifyWebhookOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ShopifyWebhookVerificationResult Verify(string payload, string? providedHmacHeader)
    {
        var sharedSecret = _optionsMonitor.CurrentValue.SharedSecret;

        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            return ShopifyWebhookVerificationResult.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(providedHmacHeader))
        {
            return ShopifyWebhookVerificationResult.MissingHeader;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHmac = Convert.ToBase64String(computedBytes);

        var providedBytes = Encoding.UTF8.GetBytes(providedHmacHeader.Trim());
        var computedCompareBytes = Encoding.UTF8.GetBytes(computedHmac);

        if (providedBytes.Length != computedCompareBytes.Length)
        {
            return ShopifyWebhookVerificationResult.Invalid;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, computedCompareBytes)
            ? ShopifyWebhookVerificationResult.Valid
            : ShopifyWebhookVerificationResult.Invalid;
    }
}
