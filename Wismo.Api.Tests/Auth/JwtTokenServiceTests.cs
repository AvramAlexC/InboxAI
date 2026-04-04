using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wismo.Api.Auth;

namespace Wismo.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private readonly JwtOptions _options = new()
    {
        SigningKey = "ThisIsATestSigningKeyThatIsLongEnoughForHmacSha256!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenMinutes = 60
    };

    private JwtTokenService CreateSut()
    {
        var monitor = Mock.Of<IOptionsMonitor<JwtOptions>>(m => m.CurrentValue == _options);
        return new JwtTokenService(monitor);
    }

    [Fact]
    public void CreateToken_ReturnsNonEmptyAccessToken()
    {
        var sut = CreateSut();

        var result = sut.CreateToken("user@test.com", "Test User", 42);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateToken_SetsCorrectResponseFields()
    {
        var sut = CreateSut();

        var result = sut.CreateToken("user@test.com", "Test User", 42);

        result.Email.Should().Be("user@test.com");
        result.UserName.Should().Be("Test User");
        result.TenantId.Should().Be(42);
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void CreateToken_TokenContainsExpectedClaims()
    {
        var sut = CreateSut();
        var result = sut.CreateToken("user@test.com", "Test User", 7);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Issuer.Should().Be("TestIssuer");
        token.Audiences.Should().Contain("TestAudience");
        token.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "7");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "user@test.com");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == "Test User");
    }

    [Fact]
    public void CreateToken_ExpiresAfterConfiguredMinutes()
    {
        _options.AccessTokenMinutes = 30;
        var sut = CreateSut();

        var before = DateTime.UtcNow;
        var result = sut.CreateToken("user@test.com", "User", 1);

        result.ExpiresAtUtc.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(5));
    }
}
