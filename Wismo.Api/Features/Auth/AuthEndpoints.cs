using FluentValidation;
using Wismo.Api.Auth;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;
using Wismo.Api.Repositories;

namespace Wismo.Api.Features.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Email, string Password, string Name, int TenantId);

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(4);
    }
}

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2);
        RuleFor(x => x.TenantId).GreaterThan(0);
    }
}

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IStoreUserRepository userRepository,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService,
            CancellationToken cancellationToken) =>
        {
            var user = await userRepository.GetActiveByEmailAsync(request.Email, cancellationToken);

            if (user is null)
            {
                return Results.Unauthorized();
            }

            var validPassword = passwordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt);
            if (!validPassword)
            {
                return Results.Unauthorized();
            }

            var loginResponse = jwtTokenService.CreateToken(user.Email, user.Name, user.TenantId);
            return Results.Ok(loginResponse);
        });

        group.MapPost("/bootstrap", async (
            RegisterRequest request,
            IStoreUserRepository userRepository,
            ITenantRepository tenantRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            CancellationToken cancellationToken) =>
        {
            var anyUsers = await userRepository.AnyAsync(cancellationToken);
            if (anyUsers)
            {
                return Results.Conflict(new { Message = "Bootstrap este disponibil doar cand nu exista niciun user." });
            }

            var tenantExists = await tenantRepository.ExistsAsync(request.TenantId, cancellationToken);
            if (!tenantExists)
            {
                return Results.BadRequest(new { Message = "Tenantul nu exista." });
            }

            var existingEmail = await userRepository.EmailExistsAsync(request.Email, cancellationToken);
            if (existingEmail)
            {
                return Results.Conflict(new { Message = "Email deja folosit." });
            }

            var (hash, salt) = passwordHasher.Hash(request.Password);

            var user = new StoreUser
            {
                Email = request.Email,
                Name = request.Name,
                TenantId = request.TenantId,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true
            };

            userRepository.Add(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/auth/users/{user.Id}", new { user.Id, user.Email, user.Name, user.TenantId });
        });

        group.MapPost("/register", async (
            RegisterRequest request,
            IStoreUserRepository userRepository,
            ITenantRepository tenantRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value != request.TenantId)
            {
                return Results.Forbid();
            }

            var tenantExists = await tenantRepository.ExistsAsync(request.TenantId, cancellationToken);
            if (!tenantExists)
            {
                return Results.BadRequest(new { Message = "Tenantul nu exista." });
            }

            var existingEmail = await userRepository.EmailExistsAsync(request.Email, cancellationToken);
            if (existingEmail)
            {
                return Results.Conflict(new { Message = "Email deja folosit." });
            }

            var (hash, salt) = passwordHasher.Hash(request.Password);

            var user = new StoreUser
            {
                Email = request.Email,
                Name = request.Name,
                TenantId = request.TenantId,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true
            };

            userRepository.Add(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/auth/users/{user.Id}", new { user.Id, user.Email, user.Name, user.TenantId });
        }).RequireAuthorization();

        group.MapGet("/me", (HttpContext httpContext, ITenantContext tenantContext) =>
        {
            if (!tenantContext.TenantId.HasValue)
            {
                return Results.Unauthorized();
            }

            var userName = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var email = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            return Results.Ok(new
            {
                TenantId = tenantContext.TenantId.Value,
                UserName = userName,
                Email = email
            });
        }).RequireAuthorization();
    }
}
