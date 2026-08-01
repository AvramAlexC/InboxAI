using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wismo.Api.Multitenancy;

namespace Wismo.Api;

/// <summary>
/// Used only by the EF Core tools (migrations add / script / database update).
/// The runtime connection string comes exclusively from user-secrets, so the host
/// fails fast when it is missing — which would otherwise block design-time commands.
/// The placeholder below is never connected to when scaffolding or scripting migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=WismoAiDesignTime;Trusted_Connection=True;";

    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(DesignTimeConnectionString)
            .Options;

        return new AppDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public int? TenantId => null;
    }
}
