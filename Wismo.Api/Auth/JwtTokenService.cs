using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Wismo.Api.Auth;

public interface IJwtTokenService
{
    LoginResponse CreateToken(string email, string name, int tenantId);
}

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, int TenantId, string UserName, string Email);

public sealed class JwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor) : IJwtTokenService
{
    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor = optionsMonitor;

    public LoginResponse CreateToken(string email, string name, int tenantId)
    {
        var options = _optionsMonitor.CurrentValue;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, email),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.NameIdentifier, email),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email)
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        return new LoginResponse(tokenValue, expires, tenantId, name, email);
    }
}
