using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WismoAI.Core.Services;

public record TicketRequest(string CustomerEmail, string MessageBody);
public record AiClassificationResult(string Intent, string? OrderId, string? ExtractedEmail, string DraftResponse, decimal Confidence);

public interface ITicketAiProcessor
{
    Task<AiClassificationResult> ProcessTicketAsync(TicketRequest request, CancellationToken ct = default);
}

public class OpenAIProcessorService : ITicketAiProcessor
{
    private static readonly Regex CourierAwbRegex = new(
        @"\b(?:SAMEDAY|FAN|CARGUS)\s*[:\-]\s*[A-Z0-9]{4,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OrderHashRegex = new(
        @"#[A-Z0-9_-]{3,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OrderByKeywordRegex = new(
        @"\b(?:comanda|order)\s*[:#\-]?\s*([A-Z0-9_-]{4,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LongDigitsRegex = new(
        @"\b\d{5,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"(?<![A-Z0-9._%+-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}(?![A-Z0-9._%+-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProcessorService> _logger;

    public OpenAIProcessorService(HttpClient httpClient, ILogger<OpenAIProcessorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AiClassificationResult> ProcessTicketAsync(TicketRequest request, CancellationToken ct = default)
    {
        var systemPrompt = @"You are an e-commerce support assistant for Romanian customers.
Classify message intent into one of: WISMO, REFUND, QUESTION, SPAM.
Extract orderId when present (AWB, #order, order number).
Extract email when present in the message.
Return ONLY valid JSON with keys:
{
  ""intent"": ""WISMO|REFUND|QUESTION|SPAM"",
  ""orderId"": ""string or null"",
  ""email"": ""string or null"",
  ""draftResponse"": ""Romanian reply"",
  ""confidence"": 0.0
}";

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
                    {
                        customerEmail = request.CustomerEmail,
                        message = request.MessageBody
                    })
                }
            },
            response_format = new { type = "json_object" }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/chat/completions", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("OpenAI API failed with {StatusCode}. Body: {Body}", response.StatusCode, errorBody);
                return BuildFallbackResult(request);
            }

            var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var aiContent = result?
                .RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(aiContent))
            {
                _logger.LogWarning("OpenAI response content is empty. Using fallback classification.");
                return BuildFallbackResult(request);
            }

            var parsed = ParseAiJson(aiContent);
            return NormalizeResult(parsed, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI processing failed. Using fallback classification.");
            return BuildFallbackResult(request);
        }
    }

    private static AiClassificationResult BuildFallbackResult(TicketRequest request)
        => NormalizeResult(null, request);

    private static AiClassificationResult NormalizeResult(AiClassificationResult? ai, TicketRequest request)
    {
        var orderId = NormalizeOrderId(ai?.OrderId) ?? TryExtractOrderId(request.MessageBody);
        var extractedEmail = NormalizeEmail(ai?.ExtractedEmail)
            ?? TryExtractEmail(request.MessageBody)
            ?? NormalizeEmail(request.CustomerEmail);

        var intent = NormalizeIntent(ai?.Intent, request.MessageBody);
        var draftResponse = BuildDraftResponse(ai?.DraftResponse, intent, orderId, extractedEmail);
        var confidence = NormalizeConfidence(ai?.Confidence ?? 0.25m, orderId, extractedEmail, ai is not null);

        return new AiClassificationResult(
            Intent: intent,
            OrderId: orderId,
            ExtractedEmail: extractedEmail,
            DraftResponse: draftResponse,
            Confidence: confidence);
    }

    private static AiClassificationResult? ParseAiJson(string aiContent)
    {
        try
        {
            using var parsedJson = JsonDocument.Parse(aiContent);
            var root = parsedJson.RootElement;

            var intent = TryGetString(root, "intent");
            var orderId = FirstNonEmpty(
                TryGetString(root, "orderId"),
                TryGetString(root, "order_id"));
            var email = FirstNonEmpty(
                TryGetString(root, "email"),
                TryGetString(root, "customerEmail"),
                TryGetString(root, "customer_email"));
            var draftResponse = FirstNonEmpty(
                TryGetString(root, "draftResponse"),
                TryGetString(root, "draft_response"));
            var confidence = TryGetDecimal(root, "confidence") ?? 0.25m;

            return new AiClassificationResult(intent ?? string.Empty, orderId, email, draftResponse, confidence);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeIntent(string? proposedIntent, string messageBody)
    {
        var normalized = (proposedIntent ?? string.Empty).Trim().ToUpperInvariant();

        if (normalized is "WISMO" or "REFUND" or "QUESTION" or "SPAM")
        {
            return normalized;
        }

        var lower = (messageBody ?? string.Empty).ToLowerInvariant();

        if (ContainsAny(lower, "retur", "refund", "bani inapoi", "anulez", "returnare"))
        {
            return "REFUND";
        }

        if (ContainsAny(lower, "promo", "bitcoin", "crypto", "castig", "free money", "click aici"))
        {
            return "SPAM";
        }

        if (ContainsAny(lower, "unde", "pachet", "awb", "comanda", "tracking", "status", "livrare"))
        {
            return "WISMO";
        }

        return "QUESTION";
    }

    private static decimal NormalizeConfidence(decimal rawConfidence, string? orderId, string? email, bool hasAiOutput)
    {
        var normalized = rawConfidence;

        if (normalized < 0m || normalized > 1m)
        {
            normalized = Math.Clamp(normalized / 100m, 0m, 1m);
        }

        normalized = Math.Clamp(normalized, 0m, 1m);

        if (!hasAiOutput)
        {
            normalized = Math.Min(normalized, 0.35m);
        }

        if (!string.IsNullOrWhiteSpace(orderId) || !string.IsNullOrWhiteSpace(email))
        {
            normalized = Math.Max(normalized, 0.65m);
        }

        return normalized;
    }

    private static string BuildDraftResponse(string? aiDraft, string intent, string? orderId, string? email)
    {
        var hasOrder = !string.IsNullOrWhiteSpace(orderId);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        if (!string.IsNullOrWhiteSpace(aiDraft))
        {
            var enriched = aiDraft.Trim();

            if (hasOrder && !enriched.Contains(orderId!, StringComparison.OrdinalIgnoreCase))
            {
                enriched += $" Referinta comenzii identificate: {orderId}.";
            }

            if (hasEmail && !enriched.Contains(email!, StringComparison.OrdinalIgnoreCase))
            {
                enriched += $" Email identificat: {email}.";
            }

            return enriched;
        }

        return intent switch
        {
            "WISMO" when hasOrder && hasEmail => $"Salut! Am identificat comanda {orderId} pentru adresa {email}. Verificam statusul livrarii si revenim in scurt timp.",
            "WISMO" when hasOrder => $"Salut! Am identificat comanda {orderId}. Verificam statusul livrarii si revenim in scurt timp.",
            "WISMO" when hasEmail => $"Salut! Am identificat emailul {email}. Verificam comanda asociata si revenim in scurt timp.",
            "WISMO" => "Salut! Ca sa verificam statusul, te rog sa ne trimiti numarul comenzii sau emailul folosit la plasare.",
            "REFUND" => "Salut! Am preluat solicitarea de retur/refund si revenim imediat cu pasii urmatori.",
            "SPAM" => "Mesajul a fost marcat automat pentru verificare suplimentara.",
            _ => "Salut! Multumim pentru mesaj. Revenim imediat cu raspunsul corect.",
        };
    }

    private static string? TryExtractOrderId(string messageBody)
    {
        if (string.IsNullOrWhiteSpace(messageBody))
        {
            return null;
        }

        var awbMatch = CourierAwbRegex.Match(messageBody);
        if (awbMatch.Success)
        {
            return Regex.Replace(awbMatch.Value.ToUpperInvariant(), @"\s+", string.Empty);
        }

        var orderHashMatch = OrderHashRegex.Match(messageBody);
        if (orderHashMatch.Success)
        {
            return orderHashMatch.Value.ToUpperInvariant();
        }

        var orderByKeywordMatch = OrderByKeywordRegex.Match(messageBody);
        if (orderByKeywordMatch.Success && orderByKeywordMatch.Groups.Count > 1)
        {
            return orderByKeywordMatch.Groups[1].Value.ToUpperInvariant();
        }

        var longDigitsMatch = LongDigitsRegex.Match(messageBody);
        if (longDigitsMatch.Success)
        {
            return longDigitsMatch.Value;
        }

        return null;
    }

    private static string? TryExtractEmail(string messageBody)
    {
        if (string.IsNullOrWhiteSpace(messageBody))
        {
            return null;
        }

        var match = EmailRegex.Match(messageBody);
        if (!match.Success)
        {
            return null;
        }

        return NormalizeEmail(match.Value);
    }

    private static string? NormalizeOrderId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length < 3 ? null : trimmed.ToUpperInvariant();
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return EmailRegex.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? TryGetString(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.String => valueElement.GetString(),
            JsonValueKind.Number => valueElement.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => valueElement.ToString()
        };
    }

    private static decimal? TryGetDecimal(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var valueElement))
        {
            return null;
        }

        if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (valueElement.ValueKind == JsonValueKind.String && decimal.TryParse(valueElement.GetString(), out var parsedStringValue))
        {
            return parsedStringValue;
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}


