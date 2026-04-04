using System.Net;
using System.Net.Http.Headers;

namespace Wismo.Api.Couriers;

public abstract class CourierStatusClientBase(HttpClient httpClient, ILogger logger) : ICourierStatusClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger _logger = logger;

    public abstract string CourierCode { get; }
    protected abstract CourierProviderOptions Options { get; }

    public async Task<CourierStatusResult?> GetStatusAsync(string awb, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(awb))
        {
            return null;
        }

        var options = Options;

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _logger.LogWarning("Courier {CourierCode} has no BaseUrl configured. Skip status check.", CourierCode);
            return null;
        }

        if (_httpClient.BaseAddress is null ||
            !_httpClient.BaseAddress.AbsoluteUri.Equals(options.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        }

        var statusPath = options.StatusPath.Replace("{awb}", Uri.EscapeDataString(awb), StringComparison.OrdinalIgnoreCase);
        var maxRetries = Math.Clamp(options.MaxRetries, 0, 5);
        var maxAttempts = maxRetries + 1;
        var baseDelayMilliseconds = Math.Clamp(options.RetryBaseDelayMilliseconds, 100, 10_000);
        var timeoutSeconds = Math.Clamp(options.RequestTimeoutSeconds, 3, 120);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = BuildStatusRequest(options, statusPath);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if (ShouldRetry(response.StatusCode) && attempt < maxAttempts)
                    {
                        _logger.LogWarning(
                            "Courier {CourierCode} returned transient {StatusCode} for AWB {Awb}. Attempt {Attempt}/{MaxAttempts}.",
                            CourierCode,
                            response.StatusCode,
                            awb,
                            attempt,
                            maxAttempts);

                        await DelayForRetryAsync(baseDelayMilliseconds, attempt, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning(
                        "Courier {CourierCode} returned {StatusCode} for AWB {Awb}. Body: {ResponseBody}",
                        CourierCode,
                        response.StatusCode,
                        awb,
                        TrimForLogs(responseBody));
                    return null;
                }

                var parsedStatus = CourierResponseParser.Parse(responseBody);

                if (parsedStatus is null)
                {
                    _logger.LogWarning("Courier {CourierCode} response for AWB {Awb} did not contain a status.", CourierCode, awb);
                }

                return parsedStatus;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        exception,
                        "Courier {CourierCode} timed out for AWB {Awb}. Attempt {Attempt}/{MaxAttempts}.",
                        CourierCode,
                        awb,
                        attempt,
                        maxAttempts);

                    await DelayForRetryAsync(baseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                _logger.LogError(exception, "Courier {CourierCode} timed out for AWB {Awb} after {MaxAttempts} attempts.", CourierCode, awb, maxAttempts);
                return null;
            }
            catch (HttpRequestException exception)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        exception,
                        "Courier {CourierCode} transient network error for AWB {Awb}. Attempt {Attempt}/{MaxAttempts}.",
                        CourierCode,
                        awb,
                        attempt,
                        maxAttempts);

                    await DelayForRetryAsync(baseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                _logger.LogError(exception, "Courier {CourierCode} request failed for AWB {Awb} after {MaxAttempts} attempts.", CourierCode, awb, maxAttempts);
                return null;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Courier {CourierCode} request failed for AWB {Awb}.", CourierCode, awb);
                return null;
            }
        }

        return null;
    }

    private HttpRequestMessage BuildStatusRequest(CourierProviderOptions options, string statusPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, statusPath);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
        }

        return request;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static async Task DelayForRetryAsync(int baseDelayMilliseconds, int attempt, CancellationToken cancellationToken)
    {
        var cappedExponent = Math.Clamp(attempt - 1, 0, 6);
        var multiplier = 1 << cappedExponent;
        var delayMs = Math.Min(baseDelayMilliseconds * multiplier, 15_000);
        await Task.Delay(delayMs, cancellationToken);
    }

    private static string TrimForLogs(string value)
    {
        const int maxLength = 1_000;
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "...");
    }
}
