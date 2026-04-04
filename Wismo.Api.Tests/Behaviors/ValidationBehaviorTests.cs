using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Wismo.Api.Behaviors;
using ValidationException = FluentValidation.ValidationException;

namespace Wismo.Api.Tests.Behaviors;

public class ValidationBehaviorTests
{
    private record TestRequest(string Name) : IRequest<string>;

    private class AlwaysValidValidator : AbstractValidator<TestRequest> { }

    private class NameRequiredValidator : AbstractValidator<TestRequest>
    {
        public NameRequiredValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    private static RequestHandlerDelegate<string> NextReturning(string value)
        => (ct) => Task.FromResult(value);

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            Enumerable.Empty<IValidator<TestRequest>>());

        var result = await behavior.Handle(
            new TestRequest("test"),
            NextReturning("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            new IValidator<TestRequest>[] { new AlwaysValidValidator() });

        var result = await behavior.Handle(
            new TestRequest("test"),
            NextReturning("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            new IValidator<TestRequest>[] { new NameRequiredValidator() });

        var act = () => behavior.Handle(
            new TestRequest(""),
            NextReturning("should not reach"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.Any(e => e.PropertyName == "Name"));
    }
}
