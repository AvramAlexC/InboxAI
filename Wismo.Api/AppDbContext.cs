using Microsoft.EntityFrameworkCore;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;

namespace Wismo.Api;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public int? CurrentTenantId => _tenantContext.TenantId;

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SupportTicket> SupportTickets { get; set; }
    public DbSet<StoreUser> StoreUsers { get; set; }
    public DbSet<ShopifyStoreConnection> ShopifyStoreConnections { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupportTicket>()
            .HasQueryFilter(ticket => CurrentTenantId.HasValue && ticket.TenantId == CurrentTenantId.Value);

        modelBuilder.Entity<StoreUser>()
            .HasIndex(user => user.Email)
            .IsUnique();

        modelBuilder.Entity<ShopifyStoreConnection>()
            .HasIndex(connection => connection.ShopDomain)
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}
