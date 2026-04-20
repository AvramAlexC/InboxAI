using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wismo.Api.Couriers;
using Wismo.Api.Models;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;

namespace Wismo.Api.Tests.Couriers;

public class AwbStatusSyncServiceTests
{
    private readonly Mock<ISupportTicketRepository> _ticketRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOptionsMonitor<AwbTrackingOptions>> _options = new();
    private readonly Mock<ILogger<AwbStatusSyncService>> _logger = new();
    private readonly Mock<ITenantNotificationService> _notifications = new();
    private readonly AwbTrackingOptions _settings = new()
    {
        InTransitStatuses = new List<string> { "InTransit" },
        MaxParallelRequests = 5,
    };

    public AwbStatusSyncServiceTests()
    {
        _options.SetupGet(x => x.CurrentValue).Returns(_settings);
    }

    private AwbStatusSyncService CreateSut(params ICourierStatusClient[] couriers)
        => new(
            _ticketRepository.Object,
            _unitOfWork.Object,
            couriers,
            _options.Object,
            _logger.Object,
            _notifications.Object);

    private static Mock<ICourierStatusClient> CreateCourier(string code)
    {
        var mock = new Mock<ICourierStatusClient>();
        mock.SetupGet(x => x.CourierCode).Returns(code);
        return mock;
    }

    private static SupportTicket Ticket(int id, int tenantId, string orderNumber, string status = "InTransit")
        => new()
        {
            Id = id,
            TenantId = tenantId,
            OrderNumber = orderNumber,
            Status = status,
        };

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenNoConfiguredStatuses_ReturnsZeroWithoutQuerying()
    {
        _settings.InTransitStatuses = new List<string>();
        var sut = CreateSut();

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        _ticketRepository.Verify(
            x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenNoTicketsInTransit_ReturnsZero()
    {
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket>());
        var sut = CreateSut();

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_DeduplicatesAndIgnoresBlankStatuses()
    {
        _settings.InTransitStatuses = new List<string> { "InTransit", "intransit", "  ", "" };
        string[]? captured = null;
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string[], CancellationToken>((s, _) => captured = s)
            .ReturnsAsync(new List<SupportTicket>());
        var sut = CreateSut();

        await sut.SyncInTransitStatusesAsync();

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(1);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenAwbFormatInvalid_DoesNotCallCourierAndDoesNotSave()
    {
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { Ticket(1, 10, "no-format-here") });
        var courier = CreateCourier("SAMEDAY");
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        courier.Verify(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenCourierCodeUnknown_SkipsTicket()
    {
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { Ticket(1, 10, "DHL:123") });
        var courier = CreateCourier("SAMEDAY");
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        courier.Verify(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenCourierReturnsNull_NoUpdate()
    {
        var ticket = Ticket(1, 10, "SAMEDAY:123");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync("123", It.IsAny<CancellationToken>())).ReturnsAsync((CourierStatusResult?)null);
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        ticket.Status.Should().Be("InTransit");
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenStatusUnchanged_NoSave()
    {
        var ticket = Ticket(1, 10, "SAMEDAY:123", status: "InTransit");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierStatusResult("In transit"));
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(
            x => x.NotifyTenantsDashboardUpdatedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenStatusChanges_UpdatesAndSavesAndNotifies()
    {
        var ticket = Ticket(1, 10, "SAMEDAY:123", status: "InTransit");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierStatusResult("Livrat"));
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(1);
        ticket.Status.Should().Be("Delivered");
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(
            x => x.NotifyTenantsDashboardUpdatedAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == 10),
                "awb-status-sync",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_AggregatesTenantsAcrossUpdatedTickets()
    {
        var ticketA = Ticket(1, 10, "SAMEDAY:1", status: "InTransit");
        var ticketB = Ticket(2, 20, "SAMEDAY:2", status: "InTransit");
        var ticketC = Ticket(3, 10, "SAMEDAY:3", status: "InTransit");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticketA, ticketB, ticketC });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierStatusResult("Livrat"));
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(3);
        _notifications.Verify(
            x => x.NotifyTenantsDashboardUpdatedAsync(
                It.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 10, 20 })),
                "awb-status-sync",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenCourierThrows_ContinuesWithoutFailingJob()
    {
        var ticket = Ticket(1, 10, "SAMEDAY:123", status: "InTransit");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync("123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(0);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_WhenCancellationRequested_PropagatesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ticket = Ticket(1, 10, "SAMEDAY:123");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), cts.Token))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var sut = CreateSut(courier.Object);

        var act = () => sut.SyncInTransitStatusesAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SyncInTransitStatusesAsync_CourierLookupIsCaseInsensitive()
    {
        var ticket = Ticket(1, 10, "sameday:123", status: "InTransit");
        _ticketRepository
            .Setup(x => x.GetByStatusesIgnoringFiltersAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket> { ticket });
        var courier = CreateCourier("SAMEDAY");
        courier.Setup(x => x.GetStatusAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierStatusResult("Livrat"));
        var sut = CreateSut(courier.Object);

        var result = await sut.SyncInTransitStatusesAsync();

        result.Should().Be(1);
        ticket.Status.Should().Be("Delivered");
    }
}
