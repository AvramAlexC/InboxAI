using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using Quartz;
using System.Net;
using System.Reflection;
using System.Text;
using Wismo.Api;
using Wismo.Api.Auth;
using Wismo.Api.Behaviors;
using Wismo.Api.Couriers;
using Wismo.Api.Features.Auth;
using Wismo.Api.Features.Dashboard;
using Wismo.Api.Features.Shopify;
using Wismo.Api.Jobs;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;
using WismoAI.Api.Features.Tickets;
using WismoAI.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Lets the app run as a Windows Service in production; no-op when started from a console.
builder.Host.UseWindowsService();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = jwtOptions.SigningKey;

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
{
    signingKey = "LOCAL_DEV_SIGNING_KEY_CHANGE_ME_12345678901234567890";
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var requestPath = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    requestPath.StartsWithSegments(TenantDashboardHub.HubPath))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

builder.Services.Configure<CourierIntegrationOptions>(builder.Configuration.GetSection("Couriers"));
builder.Services.Configure<AwbTrackingOptions>(builder.Configuration.GetSection("AwbTracking"));
builder.Services.Configure<ShopifyWebhookOptions>(builder.Configuration.GetSection("Shopify:Webhook"));
builder.Services.AddSingleton<IShopifyWebhookVerifier, ShopifyWebhookVerifier>();
builder.Services.Configure<ShopifyOAuthOptions>(builder.Configuration.GetSection("Shopify:OAuth"));
builder.Services.Configure<OpenAiResilienceOptions>(builder.Configuration.GetSection("OpenAI:Resilience"));

builder.Services.AddHttpClient<SamedayCourierClient>();
builder.Services.AddHttpClient<FanCourierClient>();
builder.Services.AddHttpClient<CargusCourierClient>();

builder.Services.AddScoped<ICourierStatusClient>(serviceProvider => serviceProvider.GetRequiredService<SamedayCourierClient>());
builder.Services.AddScoped<ICourierStatusClient>(serviceProvider => serviceProvider.GetRequiredService<FanCourierClient>());
builder.Services.AddScoped<ICourierStatusClient>(serviceProvider => serviceProvider.GetRequiredService<CargusCourierClient>());
builder.Services.AddScoped<IAwbStatusSyncService, AwbStatusSyncService>();
builder.Services.AddSingleton<ITenantNotificationService, TenantNotificationService>();
builder.Services.AddSignalR();

// The AWB sync job is only worth scheduling when a courier is actually reachable.
// With the example/placeholder base URLs every run fails and floods the logs.
var courierOptions = builder.Configuration.GetSection("Couriers").Get<CourierIntegrationOptions>()
    ?? new CourierIntegrationOptions();
var courierBaseUrls = new[]
{
    courierOptions.Sameday.BaseUrl,
    courierOptions.FanCourier.BaseUrl,
    courierOptions.Cargus.BaseUrl
};
var awbSyncEnabled = courierBaseUrls.Any(IsUsableCourierBaseUrl);

builder.Services.AddQuartz(quartz =>
{
    if (awbSyncEnabled)
    {
        var jobKey = new JobKey("awb-status-update-job");
        quartz.AddJob<AwbStatusUpdateJob>(options => options.WithIdentity(jobKey));

        var cronExpression = builder.Configuration["AwbTracking:Cron"];
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            cronExpression = "0 */10 * * * ?";
        }

        quartz.AddTrigger(options => options
            .ForJob(jobKey)
            .WithIdentity("awb-status-update-trigger")
            .WithCronSchedule(cronExpression, cron => cron.InTimeZone(TimeZoneInfo.Local)));
    }
});

static bool IsUsableCourierBaseUrl(string? baseUrl)
{
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        return false;
    }

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return !uri.Host.EndsWith(".example", StringComparison.OrdinalIgnoreCase)
        && !uri.Host.Equals("example", StringComparison.OrdinalIgnoreCase);
}

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString)
    || connectionString.Contains("<set", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:Default' is not configured. " +
        "Set it via user-secrets, e.g.: dotnet user-secrets set \"ConnectionStrings:Default\" " +
        "\"<azure-sql-connection-string>\" --project Wismo.Api");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(maxRetryCount: 8, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
        sql.CommandTimeout(60);
    }));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
builder.Services.AddScoped<IStoreUserRepository, StoreUserRepository>();
builder.Services.AddScoped<IShopifyStoreConnectionRepository, ShopifyStoreConnectionRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddExceptionHandler<WismoAI.Api.Middleware.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<ITicketAiProcessor, OpenAIProcessorService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");

    var apiKey = builder.Configuration["OpenAI:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("<set", StringComparison.OrdinalIgnoreCase))
    {
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }
})
.AddPolicyHandler((serviceProvider, _) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenAiResilienceOptions>>().Value;
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenAiRetryPolicy");

    var retryCount = Math.Max(1, options.RetryCount);
    var baseDelayMs = Math.Max(100, options.BaseDelayMilliseconds);
    var jitterMs = Math.Max(1, options.JitterMilliseconds);

    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount,
            attempt =>
            {
                var exponentialBackoffMs = baseDelayMs * Math.Pow(2, attempt - 1);
                var jitter = Random.Shared.Next(0, jitterMs);
                return TimeSpan.FromMilliseconds(exponentialBackoffMs + jitter);
            },
            (outcome, delay, attempt, _) =>
            {
                var reason = outcome.Exception?.Message
                    ?? outcome.Result?.StatusCode.ToString()
                    ?? "unknown";

                logger.LogWarning(
                    "OpenAI retry {Attempt}/{RetryCount} in {DelayMs} ms. Reason={Reason}.",
                    attempt,
                    retryCount,
                    delay.TotalMilliseconds,
                    reason);
            });
})
.AddPolicyHandler((serviceProvider, _) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenAiResilienceOptions>>().Value;
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenAiCircuitBreakerPolicy");

    var failuresBeforeBreak = Math.Max(2, options.CircuitBreakerFailures);
    var breakDuration = TimeSpan.FromSeconds(Math.Max(5, options.CircuitBreakSeconds));

    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .CircuitBreakerAsync(
            failuresBeforeBreak,
            breakDuration,
            (outcome, duration) =>
            {
                var reason = outcome.Exception?.Message
                    ?? outcome.Result?.StatusCode.ToString()
                    ?? "unknown";

                logger.LogWarning(
                    "OpenAI circuit opened for {BreakSeconds} seconds. Reason={Reason}.",
                    duration.TotalSeconds,
                    reason);
            },
            () => logger.LogInformation("OpenAI circuit reset."),
            () => logger.LogInformation("OpenAI circuit half-open."));
})
.AddPolicyHandler((serviceProvider, _) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenAiResilienceOptions>>().Value;
    var timeoutSeconds = Math.Max(5, options.PerAttemptTimeoutSeconds);
    return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(timeoutSeconds));
});

var app = builder.Build();

if (!awbSyncEnabled)
{
    app.Logger.LogWarning(
        "AWB status sync job not scheduled: courier base URL is not configured ({CourierBaseUrls}).",
        string.Join(", ", courierBaseUrls.Select(url => string.IsNullOrWhiteSpace(url) ? "<empty>" : url)));
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    if (!db.SupportTickets.IgnoreQueryFilters().Any())
    {
        var primulClient = new Tenant { Name = "Magazin Demo", ContactEmail = "admin@magazin.ro" };
        db.Tenants.Add(primulClient);

        db.SupportTickets.Add(new SupportTicket
        {
            CustomerEmail = "client.nervos@gmail.com",
            OrderNumber = "SAMEDAY:123456789",
            Status = "InTransit",
            Tenant = primulClient
        });

        db.SaveChanges();
    }
}

app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapTicketEndpoints();
app.MapShopifyAuthEndpoints();
app.MapShopifyWebhookEndpoints();
app.MapHub<TenantDashboardHub>(TenantDashboardHub.HubPath);
app.MapHealthChecks("/health");

app.Run();
