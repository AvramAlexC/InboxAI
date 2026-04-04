using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wismo.Api.Auth;
using Wismo.Api.Models;
using Wismo.Api.Repositories;

namespace Wismo.Api.Features.Shopify;

public static class ShopifyAuthEndpoints
{
    private const string DefaultScopes = "read_orders";
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(15);

    public static void MapShopifyAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopify/connect")
            .WithTags("Shopify OAuth");

        group.MapGet("/install", HandleInstall);
        group.MapGet("/callback", HandleCallback);
    }

    private static IResult HandleInstall(string shop, IOptionsMonitor<ShopifyOAuthOptions> optionsMonitor)
    {
        var options = optionsMonitor.CurrentValue;
        var normalizedShop = NormalizeShopDomain(shop);

        if (!IsValidShopDomain(normalizedShop))
        {
            return Results.BadRequest(new { Message = "Shop domain invalid. Exemplu: demo-store.myshopify.com" });
        }

        if (!HasOAuthConfig(options, out var configError))
        {
            return Results.Problem(configError, statusCode: StatusCodes.Status500InternalServerError);
        }

        var state = BuildSignedState(normalizedShop, options.ClientSecret);
        var redirectUri = BuildOAuthRedirectUri(options);
        var scopes = string.IsNullOrWhiteSpace(options.Scopes) ? DefaultScopes : options.Scopes.Trim();

        var installUrl = $"https://{normalizedShop}/admin/oauth/authorize" +
                         $"?client_id={Uri.EscapeDataString(options.ClientId)}" +
                         $"&scope={Uri.EscapeDataString(scopes)}" +
                         $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                         $"&state={Uri.EscapeDataString(state)}";

        return Results.Redirect(installUrl);
    }

    private static async Task<IResult> HandleCallback(
        HttpRequest request,
        ITenantRepository tenantRepository,
        IStoreUserRepository userRepository,
        IShopifyStoreConnectionRepository connectionRepository,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<ShopifyOAuthOptions> optionsMonitor,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ShopifyOAuth");
        var options = optionsMonitor.CurrentValue;

        if (!HasOAuthConfig(options, out var configError))
        {
            return Results.Problem(configError, statusCode: StatusCodes.Status500InternalServerError);
        }

        var shop = NormalizeShopDomain(request.Query["shop"].FirstOrDefault());
        var code = request.Query["code"].FirstOrDefault();
        var hmac = request.Query["hmac"].FirstOrDefault();
        var state = request.Query["state"].FirstOrDefault();
        var oauthError = request.Query["error"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            return RedirectWithError(options.FrontendUrl, $"Shopify OAuth failed: {oauthError}");
        }

        if (!IsValidShopDomain(shop) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(hmac) || string.IsNullOrWhiteSpace(state))
        {
            return RedirectWithError(options.FrontendUrl, "Parametri OAuth lipsa sau invalizi.");
        }

        if (!IsValidSignedState(state, shop, options.ClientSecret))
        {
            return RedirectWithError(options.FrontendUrl, "State invalid sau expirat.");
        }

        if (!IsCallbackHmacValid(request.Query, hmac, options.ClientSecret))
        {
            logger.LogWarning("Invalid Shopify callback HMAC for shop {Shop}", shop);
            return RedirectWithError(options.FrontendUrl, "Semnatura callback invalida.");
        }

        var tokenResult = await ExchangeCodeForTokenAsync(httpClientFactory, options, shop, code, cancellationToken);
        if (tokenResult is null)
        {
            return RedirectWithError(options.FrontendUrl, "Nu am putut obtine access token Shopify.");
        }

        var shopInfo = await GetShopInfoAsync(httpClientFactory, options, shop, tokenResult.AccessToken, cancellationToken);
        if (shopInfo is null)
        {
            return RedirectWithError(options.FrontendUrl, "Nu am putut citi datele magazinului Shopify.");
        }

        var provisioned = await ProvisionTenantAsync(
            tenantRepository,
            userRepository,
            connectionRepository,
            unitOfWork,
            passwordHasher,
            shop,
            tokenResult.AccessToken,
            tokenResult.Scope,
            shopInfo,
            cancellationToken);

        var webhookAddress = BuildWebhookAddress(options);
        var webhookRegistered = await RegisterOrderCreateWebhookAsync(
            httpClientFactory,
            options,
            shop,
            tokenResult.AccessToken,
            webhookAddress,
            logger,
            cancellationToken);

        if (!webhookRegistered)
        {
            logger.LogWarning("Shopify webhook registration returned non-success for shop {Shop}", shop);
        }

        var login = jwtTokenService.CreateToken(provisioned.User.Email, provisioned.User.Name, provisioned.Tenant.Id);
        var frontendRedirectUrl = BuildFrontendSuccessRedirect(options.FrontendUrl, login);

        return Results.Redirect(frontendRedirectUrl);
    }

    private static async Task<ProvisionedTenant> ProvisionTenantAsync(
        ITenantRepository tenantRepository,
        IStoreUserRepository userRepository,
        IShopifyStoreConnectionRepository connectionRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        string shopDomain,
        string accessToken,
        string scopes,
        ShopifyShopInfo shopInfo,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var connection = await connectionRepository.GetByShopDomainWithTenantAsync(shopDomain, cancellationToken);

        Tenant tenant;

        if (connection is null)
        {
            tenant = new Tenant
            {
                Name = string.IsNullOrWhiteSpace(shopInfo.Name) ? shopDomain : shopInfo.Name,
                ContactEmail = string.IsNullOrWhiteSpace(shopInfo.Email) ? $"owner@{shopDomain}" : shopInfo.Email
            };

            tenantRepository.Add(tenant);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            connection = new ShopifyStoreConnection
            {
                ShopDomain = shopDomain,
                TenantId = tenant.Id,
                AccessToken = accessToken,
                Scopes = scopes,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            connectionRepository.Add(connection);
        }
        else
        {
            tenant = connection.Tenant ?? (await tenantRepository.GetByIdAsync(connection.TenantId, cancellationToken))!;
            connection.AccessToken = accessToken;
            connection.Scopes = scopes;
            connection.IsActive = true;
            connection.UpdatedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(shopInfo.Name))
        {
            tenant.Name = shopInfo.Name;
        }

        if (!string.IsNullOrWhiteSpace(shopInfo.Email))
        {
            tenant.ContactEmail = shopInfo.Email;
        }

        var ownerEmail = BuildTenantOwnerEmail(shopDomain, tenant.Id);
        var user = await userRepository.GetByEmailAsync(ownerEmail, cancellationToken);

        if (user is null)
        {
            var (hash, salt) = passwordHasher.Hash(Guid.NewGuid().ToString("N"));
            user = new StoreUser
            {
                Email = ownerEmail,
                Name = tenant.Name,
                TenantId = tenant.Id,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true,
                CreatedAt = now
            };

            userRepository.Add(user);
        }
        else
        {
            user.Name = tenant.Name;
            user.IsActive = true;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProvisionedTenant(tenant, user);
    }

    private static async Task<ShopifyAccessTokenResult?> ExchangeCodeForTokenAsync(
        IHttpClientFactory httpClientFactory,
        ShopifyOAuthOptions options,
        string shopDomain,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"https://{shopDomain}/admin/oauth/access_token",
            new
            {
                client_id = options.ClientId,
                client_secret = options.ClientSecret,
                code = authorizationCode
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var accessToken = TryGetString(document.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var scope = TryGetString(document.RootElement, "scope") ?? string.Empty;
        return new ShopifyAccessTokenResult(accessToken, scope);
    }

    private static async Task<ShopifyShopInfo?> GetShopInfoAsync(
        IHttpClientFactory httpClientFactory,
        ShopifyOAuthOptions options,
        string shopDomain,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{shopDomain}/admin/api/{options.ApiVersion}/shop.json");
        request.Headers.TryAddWithoutValidation("X-Shopify-Access-Token", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("shop", out var shopElement) || shopElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = TryGetString(shopElement, "name") ?? shopDomain;
        var email = TryGetString(shopElement, "email") ?? string.Empty;

        return new ShopifyShopInfo(name, email);
    }

    private static async Task<bool> RegisterOrderCreateWebhookAsync(
        IHttpClientFactory httpClientFactory,
        ShopifyOAuthOptions options,
        string shopDomain,
        string accessToken,
        string webhookAddress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{shopDomain}/admin/api/{options.ApiVersion}/webhooks.json");
        request.Headers.TryAddWithoutValidation("X-Shopify-Access-Token", accessToken);
        request.Content = JsonContent.Create(new
        {
            webhook = new
            {
                topic = "orders/create",
                address = webhookAddress,
                format = "json"
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode is 409 or 422)
        {
            logger.LogInformation("Shopify webhook already exists or cannot be duplicated for {Shop}. Body={Body}", shopDomain, body);
            return true;
        }

        logger.LogWarning("Shopify webhook registration failed for {Shop}. Status={Status}. Body={Body}", shopDomain, response.StatusCode, body);
        return false;
    }

    private static bool IsCallbackHmacValid(IQueryCollection query, string providedHmac, string clientSecret)
    {
        var sortedPairs = query
            .Where(item => !item.Key.Equals("hmac", StringComparison.OrdinalIgnoreCase) &&
                           !item.Key.Equals("signature", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}");

        var payload = string.Join("&", sortedPairs);
        var computedHmac = ComputeHexHmac(clientSecret, payload);

        var providedBytes = Encoding.UTF8.GetBytes((providedHmac ?? string.Empty).Trim().ToLowerInvariant());
        var computedBytes = Encoding.UTF8.GetBytes(computedHmac);

        return providedBytes.Length == computedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, computedBytes);
    }

    private static string BuildSignedState(string shopDomain, string clientSecret)
    {
        var issuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var payload = $"{shopDomain}|{issuedAtUnix}|{nonce}";
        var signature = ComputeHexHmac(clientSecret, payload);
        var rawState = $"{payload}|{signature}";

        return Base64UrlEncode(rawState);
    }

    private static bool IsValidSignedState(string encodedState, string shopDomain, string clientSecret)
    {
        var decoded = Base64UrlDecode(encodedState);
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        var parts = decoded.Split('|');
        if (parts.Length != 4)
        {
            return false;
        }

        var stateShop = parts[0];
        if (!stateShop.Equals(shopDomain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var issuedAtUnix))
        {
            return false;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);
        if (DateTimeOffset.UtcNow - issuedAt > OAuthStateLifetime)
        {
            return false;
        }

        var payload = $"{parts[0]}|{parts[1]}|{parts[2]}";
        var expectedSignature = ComputeHexHmac(clientSecret, payload);

        var providedBytes = Encoding.UTF8.GetBytes(parts[3].ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static bool HasOAuthConfig(ShopifyOAuthOptions options, out string error)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            error = "Shopify OAuth config missing: Shopify:OAuth:ClientId / ClientSecret";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.PublicAppUrl))
        {
            error = "Shopify OAuth config missing: Shopify:OAuth:PublicAppUrl";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string BuildOAuthRedirectUri(ShopifyOAuthOptions options)
        => $"{options.PublicAppUrl.TrimEnd('/')}/api/shopify/connect/callback";

    private static string BuildWebhookAddress(ShopifyOAuthOptions options)
        => $"{options.PublicAppUrl.TrimEnd('/')}/api/webhooks/shopify/orders/create";

    private static string BuildFrontendSuccessRedirect(string frontendUrl, LoginResponse login)
    {
        var separator = frontendUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return frontendUrl.TrimEnd('/') +
               separator +
               $"accessToken={Uri.EscapeDataString(login.AccessToken)}" +
               $"&expiresAtUtc={Uri.EscapeDataString(login.ExpiresAtUtc.ToString("O"))}" +
               $"&tenantId={login.TenantId}" +
               $"&userName={Uri.EscapeDataString(login.UserName)}" +
               $"&email={Uri.EscapeDataString(login.Email)}";
    }

    private static IResult RedirectWithError(string frontendUrl, string error)
    {
        var separator = frontendUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var redirectUrl = frontendUrl.TrimEnd('/') + separator + $"oauthError={Uri.EscapeDataString(error)}";
        return Results.Redirect(redirectUrl);
    }

    private static string NormalizeShopDomain(string? shop)
        => (shop ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidShopDomain(string? shop)
    {
        if (string.IsNullOrWhiteSpace(shop))
        {
            return false;
        }

        return Regex.IsMatch(shop, "^[a-z0-9][a-z0-9-]*\\.myshopify\\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildTenantOwnerEmail(string shopDomain, int tenantId)
        => $"owner+tenant{tenantId}@{shopDomain}";

    private static string ComputeHexHmac(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Base64UrlDecode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var padded = input
            .Replace('-', '+')
            .Replace('_', '/');

        while (padded.Length % 4 != 0)
        {
            padded += "=";
        }

        try
        {
            var bytes = Convert.FromBase64String(padded);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind switch
        {
            JsonValueKind.String => propertyValue.GetString(),
            JsonValueKind.Number => propertyValue.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => propertyValue.ToString()
        };
    }

    private sealed record ShopifyAccessTokenResult(string AccessToken, string Scope);
    private sealed record ShopifyShopInfo(string Name, string Email);
    private sealed record ProvisionedTenant(Tenant Tenant, StoreUser User);
}

