using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wismo.Api;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;
using Wismo.Api.Repositories;

namespace Wismo.Api.Tests.Repositories;

public sealed class SupportTicketRepositoryTests : IDisposable
{
    private const string OrderCreatedIntent = "SHOPIFY_ORDER_CREATED";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly SupportTicketRepository _repository;

    public SupportTicketRepositoryTests()
    {
        // Real SQLite (in-memory) so the query filter + IgnoreQueryFilters behave like production.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options, new FakeTenantContext(TenantId: 1));
        _db.Database.EnsureCreated();

        // SupportTicket.TenantId is a FK to Tenant; seed the tenants the tests reference.
        _db.Tenants.AddRange(
            new Tenant { Id = 1, Name = "Tenant One", ContactEmail = "one@example.com" },
            new Tenant { Id = 2, Name = "Tenant Two", ContactEmail = "two@example.com" });
        _db.SaveChanges();

        _repository = new SupportTicketRepository(_db);
    }

    [Fact]
    public async Task FindByShopifyOrderIdIgnoringFiltersAsync_matches_ticket_when_only_numeric_id_is_known()
    {
        // Ingest persists a derived OrderNumber (from `name`) plus the numeric ShopifyOrderId.
        // A delete webhook carries ONLY the numeric id, so matching must succeed on ShopifyOrderId.
        const int tenantId = 1;
        const string numericOrderId = "450789469";

        _db.SupportTickets.Add(new SupportTicket
        {
            TenantId = tenantId,
            CustomerEmail = "buyer@example.com",
            OrderNumber = "SHOPIFY:#1001",
            ShopifyOrderId = numericOrderId,
            Intent = OrderCreatedIntent,
            Status = "New",
        });
        await _db.SaveChangesAsync();

        var ticket = await _repository.FindByShopifyOrderIdIgnoringFiltersAsync(
            tenantId, numericOrderId, OrderCreatedIntent, CancellationToken.None);

        ticket.Should().NotBeNull();
        ticket!.OrderNumber.Should().Be("SHOPIFY:#1001");
        ticket.ShopifyOrderId.Should().Be(numericOrderId);
    }

    [Fact]
    public async Task FindByShopifyOrderIdIgnoringFiltersAsync_is_scoped_to_tenant_and_intent()
    {
        const string numericOrderId = "450789469";

        // Same numeric id, different tenant — must not leak across tenants.
        _db.SupportTickets.Add(new SupportTicket
        {
            TenantId = 2,
            CustomerEmail = "other@example.com",
            OrderNumber = "SHOPIFY:#9001",
            ShopifyOrderId = numericOrderId,
            Intent = OrderCreatedIntent,
            Status = "New",
        });
        await _db.SaveChangesAsync();

        var ticket = await _repository.FindByShopifyOrderIdIgnoringFiltersAsync(
            tenantId: 1, numericOrderId, OrderCreatedIntent, CancellationToken.None);

        ticket.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed record FakeTenantContext(int? TenantId) : ITenantContext;
}
