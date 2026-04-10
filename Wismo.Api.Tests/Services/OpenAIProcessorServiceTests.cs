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

    [Fact]
    public async Task ProcessTicketAsync_EmptyAiContent_ReturnsFallback()
    {
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = "   " } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Unde e pachetul?"));

        result.Intent.Should().Be("WISMO");
        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_HttpThrows_ReturnsFallback()
    {
        var handler = new ThrowingHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var logger = Mock.Of<ILogger<OpenAIProcessorService>>();
        var sut = new OpenAIProcessorService(httpClient, logger);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Vreau refund"));

        result.Intent.Should().Be("REFUND");
        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_InvalidJsonFromAi_ReturnsFallback()
    {
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = "this is not json {{{" } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "bitcoin promo"));

        result.Intent.Should().Be("SPAM");
        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ConfidenceAsPercentage_NormalizedToDecimal()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = "#ORD999",
            email = "test@test.com",
            draftResponse = "Verificam.",
            confidence = 85
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("test@test.com", "Comanda #ORD999"));

        result.Confidence.Should().BeInRange(0m, 1m);
        result.Confidence.Should().BeGreaterOrEqualTo(0.65m);
    }

    [Fact]
    public async Task ProcessTicketAsync_NegativeConfidence_ClampedToZeroRange()
    {
        var aiPayload = new
        {
            intent = "QUESTION",
            orderId = (string?)null,
            email = (string?)null,
            draftResponse = "Raspuns.",
            confidence = -5
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Ce culori aveti?"));

        result.Confidence.Should().BeInRange(0m, 1m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ExtractsCourierAwb()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = (string?)null,
            email = (string?)null,
            draftResponse = "Verificam.",
            confidence = 0.8
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "AWB-ul meu e SAMEDAY:AWB12345"));

        result.OrderId.Should().Be("SAMEDAY:AWB12345");
    }

    [Fact]
    public async Task ProcessTicketAsync_ExtractsOrderByKeyword()
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

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "comanda ABCD1234 unde e?"));

        result.OrderId.Should().Be("ABCD1234");
    }

    [Fact]
    public async Task ProcessTicketAsync_ExtractsLongDigits()
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

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "Am plasat 9876543210 si astept"));

        result.OrderId.Should().Be("9876543210");
    }

    [Fact]
    public async Task ProcessTicketAsync_EmptyMessageBody_NoOrderIdExtracted()
    {
        var aiPayload = new
        {
            intent = "QUESTION",
            orderId = (string?)null,
            email = (string?)null,
            draftResponse = "OK.",
            confidence = 0.5
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "   "));

        result.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessTicketAsync_AiReturnsNumericAndBoolFields_ParsesCorrectly()
    {
        // This tests TryGetString branches: Number, True, False, and the default object branch
        var jsonContent = """{"intent": "WISMO", "orderId": 12345, "email": true, "draftResponse": false, "confidence": 0.9}""";
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = jsonContent } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "Unde e comanda?"));

        result.Intent.Should().Be("WISMO");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessTicketAsync_ConfidenceAsString_ParsedCorrectly()
    {
        var jsonContent = """{"intent": "WISMO", "orderId": "#TEST1", "email": "a@b.com", "draftResponse": "Verificam.", "confidence": "0.75"}""";
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = jsonContent } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("a@b.com", "Comanda #TEST1"));

        result.Confidence.Should().BeGreaterOrEqualTo(0.65m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ConfidenceMissing_DefaultsTo025()
    {
        var jsonContent = """{"intent": "QUESTION", "orderId": null, "email": null, "draftResponse": "OK."}""";
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = jsonContent } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Ce faceti?"));

        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_ConfidenceUnparseableString_FallsBackToDefault()
    {
        var jsonContent = """{"intent": "QUESTION", "orderId": null, "email": null, "draftResponse": "OK.", "confidence": "not-a-number"}""";
        var responseBody = new
        {
            choices = new[]
            {
                new { message = new { content = jsonContent } }
            }
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responseBody), Encoding.UTF8, "application/json")
        };

        var sut = CreateSut(response);

        var result = await sut.ProcessTicketAsync(new TicketRequest("no-email", "Salut"));

        result.Confidence.Should().BeLessOrEqualTo(0.35m);
    }

    [Fact]
    public async Task ProcessTicketAsync_DraftEnrichedWithOrderAndEmail()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = "#ENRICH1",
            email = "enrich@test.com",
            draftResponse = "Verificam comanda dvs acum.",
            confidence = 0.9
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("enrich@test.com", "Comanda #ENRICH1"));

        result.DraftResponse.Should().Contain("#ENRICH1");
        result.DraftResponse.Should().Contain("enrich@test.com");
    }

    [Fact]
    public async Task ProcessTicketAsync_DraftAlreadyContainsOrderAndEmail_NotDuplicated()
    {
        var aiPayload = new
        {
            intent = "WISMO",
            orderId = "#DUP1",
            email = "dup@test.com",
            draftResponse = "Am gasit comanda #DUP1 pentru dup@test.com.",
            confidence = 0.9
        };

        var sut = CreateSut(CreateOpenAiResponse(aiPayload));

        var result = await sut.ProcessTicketAsync(new TicketRequest("dup@test.com", "Comanda #DUP1"));

        var count = result.DraftResponse.Split("#DUP1").Length - 1;
        count.Should().Be(1);
    }

    [Theory]
    [InlineData("WISMO", true, true, "Am identificat comanda")]
    [InlineData("WISMO", true, false, "Am identificat comanda")]
    [InlineData("WISMO", false, true, "Am identificat emailul")]
    [InlineData("WISMO", false, false, "numarul comenzii")]
    [InlineData("REFUND", false, false, "retur/refund")]
    [InlineData("SPAM", false, false, "marcat automat")]
    [InlineData("QUESTION", false, false, "Multumim pentru mesaj")]
    public async Task ProcessTicketAsync_FallbackDraftResponse_MatchesIntentAndContext(
        string intent, bool hasOrder, bool hasEmail, string expectedFragment)
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("error")
        };

        var sut = CreateSut(response);

        var order = hasOrder ? "#FALLBACK1" : "";
        var email = hasEmail ? "fall@back.com" : "no-email";
        var message = $"{(intent == "WISMO" ? "unde e pachetul" : intent == "REFUND" ? "vreau refund" : intent == "SPAM" ? "bitcoin promo" : "ce culori aveti")} {order}";

        var result = await sut.ProcessTicketAsync(new TicketRequest(email, message));

        result.DraftResponse.Should().Contain(expectedFragment);
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused");
    }
}
