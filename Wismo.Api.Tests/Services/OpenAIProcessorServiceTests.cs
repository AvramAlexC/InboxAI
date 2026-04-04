using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WismoAI.Core.Services;

namespace Wismo.Api.Tests.Services;

public class OpenAIProcessorServiceTests
{
    private static OpenAIProcessorService CreateSut(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var logger = Mock.Of<ILogger<OpenAIProcessorService>>();
        return new OpenAIProcessorService(httpClient, logger);
    }

    private static HttpResponseMessage CreateOpenAiResponse(object aiPayload)
    {
        var responseBody = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(aiPayload)
                    }
                }
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task ProcessTicketAsync_WithValidAiResponse_ReturnsClassification()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = "#12345",
            email = "client@example.com",
            draftResponse = "Verificam comanda dvs.",
            confidence = 0.9
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("client@example.com", "Unde e comanda #12345?"));

        result.Intent.Should().Be("WISMO");
        result.OrderId.Should().Be("#12345");
        result.DraftResponse.Should().NotBeNullOrWhiteSpace();
        result.Confidence.Should().BeGreaterOrEqualTo(0.65m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ApiFails_ReturnsFallback()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("error")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Ce culori aveti?"));

        result.Should().NotBeNull();
        result.Intent.Should().Be("QUESTION");
        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ExtractsOrderIdFromMessage()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = (string?)null,
            email = (string?)null,
            draftResponse = "Verificam.",
            confidence = 0.5
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "Vreau status comanda #ABC123"));

        result.OrderId.Should().Be("#ABC123");
    }

    [Fact]
    public async Task ProcessTicketAsync_ExtractsEmailFromMessage()
    {
        var aiPayload = new
        {
            intent = "QUESTION",
            orderId = (string?)null,
            email = (string?)null,
            draftResponse = "OK",
            confidence = 0.5
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "Am plasat comanda cu hidden@email.com, vreau sa stiu statusul"));

        result.ExtractedEmail.Should().Be("hidden@email.com");
    }

    [Theory]
    [InlineData("Vreau sa returnez produsul, bani inapoi", "REFUND")]
    [InlineData("Unde este pachetul meu? tracking?", "WISMO")]
    [InlineData("Click aici free money bitcoin", "SPAM")]
    [InlineData("Ce culori aveti disponibile?", "QUESTION")]
    public async Task ProcessTicketAsync_FallbackIntentDetection(string message, string expectedIntent)
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("error")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("user@test.com", message));

        result.Intent.Should().Be(expectedIntent);
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
