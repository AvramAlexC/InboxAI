namespace Wismo.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Wismo.Api";
    public string Audience { get; set; } = "Wismo.Ui";
    public string SigningKey { get; set; } = "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_KEY_32_CHARS_MIN";
    public int AccessTokenMinutes { get; set; } = 120;
}
