using FluentValidation;
using MediatR;
using Wismo.Api.Models;
using Wismo.Api.Multitenancy;
using Wismo.Api.Realtime;
using Wismo.Api.Repositories;
using WismoAI.Core.Services;

namespace WismoAI.Api.Features.Tickets;

public record CreateTicketCommand(string CustomerEmail, string MessageBody) : IRequest<int>;

public class CreateTicketValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketValidator()
    {
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.MessageBody).NotEmpty().MinimumLength(10).WithMessage("Mesajul e prea scurt.");
    }
}

public class CreateTicketHandler : IRequestHandler<CreateTicketCommand, int>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ISupportTicketRepository _ticketRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITicketAiProcessor _aiProcessor;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantNotificationService _tenantNotificationService;

    public CreateTicketHandler(
        ITenantRepository tenantRepository,
        ISupportTicketRepository ticketRepository,
        IUnitOfWork unitOfWork,
        ITicketAiProcessor aiProcessor,
        ITenantContext tenantContext,
        ITenantNotificationService tenantNotificationService)
    {
        _tenantRepository = tenantRepository;
        _ticketRepository = ticketRepository;
        _unitOfWork = unitOfWork;
        _aiProcessor = aiProcessor;
        _tenantContext = tenantContext;
        _tenantNotificationService = tenantNotificationService;
    }

    public async Task<int> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        var currentTenantId = _tenantContext.TenantId;
        if (!currentTenantId.HasValue)
        {
            throw new ValidationException("Tenant invalid. Token-ul JWT trebuie sa contina tenant_id.");
        }

        var tenantExists = await _tenantRepository.ExistsAsync(currentTenantId.Value, cancellationToken);
        if (!tenantExists)
        {
            throw new ValidationException("Tenantul specificat nu exista.");
        }

        var aiResult = await _aiProcessor.ProcessTicketAsync(
            new TicketRequest(request.CustomerEmail, request.MessageBody),
            cancellationToken);

        var ticket = new SupportTicket
        {
            CustomerEmail = request.CustomerEmail,
            MessageBody = request.MessageBody,
            OrderNumber = aiResult.OrderId ?? (!string.IsNullOrWhiteSpace(aiResult.ExtractedEmail) ? $"EMAIL:{aiResult.ExtractedEmail}" : "N/A"),
            Intent = aiResult.Intent,
            DraftResponse = aiResult.DraftResponse,
            Status = "RequiresApproval",
            TenantId = currentTenantId.Value
        };

        _ticketRepository.Add(ticket);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _tenantNotificationService.NotifyTenantDashboardUpdatedAsync(currentTenantId.Value, "ticket-created", cancellationToken);

        return ticket.Id;
    }
}
