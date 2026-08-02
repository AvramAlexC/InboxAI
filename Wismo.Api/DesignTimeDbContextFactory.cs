using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Wismo.Api.Multitenancy;

namespace Wismo.Api;

/// <summary>
/// Builds an <see cref="AppDbContext"/> for the EF Core tools (migrations add / script /
/// database update) without starting the web host. The connection string is read from the
/// same configuration sources the host uses — appsettings files, user-secrets and
/// environment variables — so design-time commands target the same database as the runtime.
/// Commands that connect (database update, dbcontext info) therefore hit the configured
/// server; there is no local fallback to silently succeed against.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:Default' is not configured for design-time. " +
                "Set it via user-secrets, e.g.: dotnet user-secrets set \"ConnectionStrings:Default\" " +
                "\"<azure-sql-connection-string>\" --project Wismo.Api");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public int? TenantId => null;
    }
}
