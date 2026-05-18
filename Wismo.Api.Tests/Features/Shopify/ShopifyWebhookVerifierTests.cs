using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wismo.Api.Features.Shopify;

namespace Wismo.Api.Tests.Features.Shopify;

public class ShopifyWebhookVerifierTests
{
    // HMAC-SHA256(payload=Payload, key=Secret), base64-encoded.
    // Pre-computed externally so the test does not re-implement the production logic.
    // To regenerate (PowerShell):
    //   $h = New-Object System.Security.Cryptography.HMACSHA256
    //   $h.Key = [Text.Encoding]::UTF8.GetBytes("test-secret")
    //   [Convert]::ToBase64String($h.ComputeHash([Text.Encoding]::UTF8.GetBytes("test-payload")))
    private const string Payload = "test-payload";
    private const string Secret = "test-secret";
    private const string ValidHmacForPayload = "WxJGfXxEhVV3nnDXYgQQXGfSfRyZHzCAwZcy+awZiO8=";

    private static IShopifyWebhookVerifier CreateSut(string? sharedSecret)
    {
        var monitor = new Mock<IOptionsMonitor<ShopifyWebhookOptions>>();
        monitor.Setup(x => x.CurrentValue)
            .Returns(new ShopifyWebhookOptions { SharedSecret = sharedSecret ?? string.Empty });
        return new ShopifyWebhookVerifier(monitor.Object);
    }

    [Fact]
    public void Verify_CorrectSignature_ReturnsValid()
    {
        var sut = CreateSut(Secret);

        var result = sut.Verify(Payload, ValidHmacForPayload);

        result.Should().Be(ShopifyWebhookVerificationResult.Valid);
    }

    [Fact]
    public void Verify_WrongSignature_ReturnsInvalid()
    {
        var sut = CreateSut(Secret);

        var result = sut.Verify(Payload, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        result.Should().Be(ShopifyWebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_NullHeader_ReturnsMissingHeader()
    {
        var sut = CreateSut(Secret);

        var result = sut.Verify(Payload, null);

        result.Should().Be(ShopifyWebhookVerificationResult.MissingHeader);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_EmptyOrWhitespaceHeader_ReturnsMissingHeader(string header)
    {
        var sut = CreateSut(Secret);

        var result = sut.Verify(Payload, header);

        result.Should().Be(ShopifyWebhookVerificationResult.MissingHeader);
    }

    [Fact]
    public void Verify_PayloadTamperedButHmacReused_ReturnsInvalid()
    {
        var sut = CreateSut(Secret);
        var tamperedPayload = Payload + "X";

        var result = sut.Verify(tamperedPayload, ValidHmacForPayload);

        result.Should().Be(ShopifyWebhookVerificationResult.Invalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_SecretNotConfigured_ReturnsNotConfigured(string? secret)
    {
        var sut = CreateSut(secret);

        var result = sut.Verify(Payload, ValidHmacForPayload);

        result.Should().Be(ShopifyWebhookVerificationResult.NotConfigured);
    }
}
