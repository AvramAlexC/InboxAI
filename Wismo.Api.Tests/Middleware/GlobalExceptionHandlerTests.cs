using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using WismoAI.Api.Middleware;

namespace Wismo.Api.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _logger = new();
    private readonly GlobalExceptionHandler _sut;

    public GlobalExceptionHandlerTests()
    {
        _sut = new GlobalExceptionHandler(_logger.Object);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task TryHandleAsync_AlwaysReturnsTrue()
    {
        var context = CreateContext();

        var handled = await _sut.TryHandleAsync(context, new Exception("x"), CancellationToken.None);

        handled.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_ForGenericException_Returns500ProblemDetails()
    {
        var context = CreateContext("/api/things");

        await _sut.TryHandleAsync(context, new InvalidOperationException("oops"), CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var body = await ReadBodyAsync(context);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.GetProperty("instance").GetString().Should().Be("/api/things");
        body.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TryHandleAsync_ForValidationException_Returns400WithGroupedErrors()
    {
        var context = CreateContext("/api/tickets");
        var failures = new List<ValidationFailure>
        {
            new("Email", "Email is required"),
            new("Email", "Email must be valid"),
            new("Body", "Body is required"),
        };
        var ex = new ValidationException(failures);

        await _sut.TryHandleAsync(context, ex, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadBodyAsync(context);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Validation Failed");
        body.GetProperty("instance").GetString().Should().Be("/api/tickets");

        var errors = body.GetProperty("errors");
        var emailErrors = errors.GetProperty("Email").EnumerateArray().Select(e => e.GetString()!).ToArray();
        emailErrors.Should().BeEquivalentTo(new[] { "Email is required", "Email must be valid" });
        var bodyErrors = errors.GetProperty("Body").EnumerateArray().Select(e => e.GetString()!).ToArray();
        bodyErrors.Should().BeEquivalentTo(new[] { "Body is required" });
    }

    [Fact]
    public async Task TryHandleAsync_LogsExceptionAtErrorLevel()
    {
        var context = CreateContext();
        var ex = new InvalidOperationException("bang");

        await _sut.TryHandleAsync(context, ex, CancellationToken.None);

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TryHandleAsync_WritesJsonContentType()
    {
        var context = CreateContext();

        await _sut.TryHandleAsync(context, new Exception("x"), CancellationToken.None);

        context.Response.ContentType.Should().StartWith("application/json");
    }
}
