using FluentAssertions;
using FluentValidation.TestHelper;
using WismoAI.Api.Features.Tickets;

namespace Wismo.Api.Tests.Features.Tickets;

public class CreateTicketValidatorTests
{
    private readonly CreateTicketValidator _sut = new();

    [Fact]
    public void ValidCommand_PassesValidation()
    {
        var command = new CreateTicketCommand("customer@example.com", "I need help with my order please");

        var result = _sut.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    public void InvalidEmail_FailsValidation(string email)
    {
        var command = new CreateTicketCommand(email, "Valid message body here");

        var result = _sut.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CustomerEmail);
    }

    [Fact]
    public void EmptyMessageBody_FailsValidation()
    {
        var command = new CreateTicketCommand("user@test.com", "");

        var result = _sut.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.MessageBody);
    }

    [Fact]
    public void MessageBodyTooShort_FailsValidation()
    {
        var command = new CreateTicketCommand("user@test.com", "short");

        var result = _sut.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.MessageBody);
    }

    [Fact]
    public void MessageBodyExactly10Chars_PassesValidation()
    {
        var command = new CreateTicketCommand("user@test.com", "1234567890");

        var result = _sut.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.MessageBody);
    }
}
