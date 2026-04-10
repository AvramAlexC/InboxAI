using FluentAssertions;
using FluentValidation;
using Moq;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;
using WismoAI.Api.Features.Tickets;
using WismoAI.Core.Services;

namespace Wismo.Api.Tests.Features.Tickets;

public class CreateTicketHandlerTests
{
    private const int DefaultTenantId = 1;

    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<ISupportTicketRepository> _ticketRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITicketAiProcessor> _aiProcessor = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ITenantNotificationService> _notificationService = new();
    private readonly CreateTicketHandler _sut;

    public CreateTicketHandlerTests()
    {
        _tenantContext.Setup(x => x.TenantId).Returns(DefaultTenantId);
        _tenantRepo.Setup(x => x.ExistsAsync(DefaultTenantId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiProcessor
            .Setup(x => x.ProcessTicketAsync(It.IsAny<TicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult("WISMO", "#ORD123", "test@test.com", "Verificam.", 0.9m));

        _sut = new(
            _tenantRepo.Object,
            _ticketRepo.Object,
            _unitOfWork.Object,
            _aiProcessor.Object,
            _tenantContext.Object,
            _notificationService.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesTicketAndSaves()
    {
        var result = await _sut.Handle(new CreateTicketCommand("customer@test.com", "Unde este comanda mea?"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t =>
            t.CustomerEmail == "customer@test.com" &&
            t.MessageBody == "Unde este comanda mea?" &&
            t.Status == "RequiresApproval" &&
            t.TenantId == DefaultTenantId
        )), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_NotifiesTenantDashboard()
    {
        await _sut.Handle(new CreateTicketCommand("customer@test.com", "Unde este comanda mea?"), CancellationToken.None);

        _notificationService.Verify(x => x.NotifyTenantDashboardUpdatedAsync(DefaultTenantId, "ticket-created", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullTenantId_ThrowsValidationException()
    {
        _tenantContext.Setup(x => x.TenantId).Returns((int?)null);

        var act = () => _sut.Handle(new CreateTicketCommand("customer@test.com", "Unde este comanda mea?"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*tenant_id*");
    }

    [Fact]
    public async Task Handle_TenantDoesNotExist_ThrowsValidationException()
    {
        _tenantContext.Setup(x => x.TenantId).Returns(99);
        _tenantRepo.Setup(x => x.ExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _sut.Handle(new CreateTicketCommand("customer@test.com", "Unde este comanda mea?"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*nu exista*");
    }

    [Fact]
    public async Task Handle_AiReturnsOrderId_SetsOrderNumber()
    {
        await _sut.Handle(new CreateTicketCommand("a@b.com", "Comanda mea #ORD123"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t => t.OrderNumber == "#ORD123")));
    }

    [Fact]
    public async Task Handle_AiReturnsNoOrderId_ButHasEmail_SetsEmailPrefix()
    {
        _aiProcessor
            .Setup(x => x.ProcessTicketAsync(It.IsAny<TicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult("WISMO", null, "extracted@mail.com", "Verificam.", 0.9m));

        await _sut.Handle(new CreateTicketCommand("a@b.com", "Vreau sa stiu statusul"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t => t.OrderNumber == "EMAIL:extracted@mail.com")));
    }

    [Fact]
    public async Task Handle_AiReturnsNoOrderIdAndNoEmail_SetsNA()
    {
        _aiProcessor
            .Setup(x => x.ProcessTicketAsync(It.IsAny<TicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult("WISMO", null, null, "Verificam.", 0.9m));

        await _sut.Handle(new CreateTicketCommand("a@b.com", "Vreau sa stiu statusul"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t => t.OrderNumber == "N/A")));
    }

    [Fact]
    public async Task Handle_AiReturnsWhitespaceEmail_SetsNA()
    {
        _aiProcessor
            .Setup(x => x.ProcessTicketAsync(It.IsAny<TicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult("WISMO", null, "   ", "Verificam.", 0.9m));

        await _sut.Handle(new CreateTicketCommand("a@b.com", "Vreau sa stiu statusul"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t => t.OrderNumber == "N/A")));
    }

    [Fact]
    public async Task Handle_SetsIntentAndDraftFromAi()
    {
        _aiProcessor
            .Setup(x => x.ProcessTicketAsync(It.IsAny<TicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult("REFUND", "#R1", null, "Am preluat solicitarea de retur.", 0.9m));

        await _sut.Handle(new CreateTicketCommand("a@b.com", "Vreau banii inapoi"), CancellationToken.None);

        _ticketRepo.Verify(x => x.Add(It.Is<SupportTicket>(t =>
            t.Intent == "REFUND" &&
            t.DraftResponse == "Am preluat solicitarea de retur."
        )));
    }

    [Fact]
    public async Task Handle_PassesCorrectRequestToAiProcessor()
    {
        await _sut.Handle(new CreateTicketCommand("specific@email.com", "Mesajul specific"), CancellationToken.None);

        _aiProcessor.Verify(x => x.ProcessTicketAsync(
            It.Is<TicketRequest>(r => r.CustomerEmail == "specific@email.com" && r.MessageBody == "Mesajul specific"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SavesBeforeNotifying()
    {
        var callOrder = new List<string>();
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("save"))
            .ReturnsAsync(0);
        _notificationService.Setup(x => x.NotifyTenantDashboardUpdatedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("notify"))
            .Returns(Task.CompletedTask);

        await _sut.Handle(new CreateTicketCommand("a@b.com", "Unde este comanda?"), CancellationToken.None);

        callOrder.Should().ContainInOrder("save", "notify");
    }
}
