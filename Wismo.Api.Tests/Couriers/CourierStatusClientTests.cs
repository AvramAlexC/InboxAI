using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wismo.Api.Couriers;

namespace Wismo.Api.Tests.Couriers;

public class CourierStatusClientTests
{
    private static CourierIntegrationOptions BuildOptions(CourierProviderOptions sameday) =>
        new() { Sameday = sameday };

    private static CourierProviderOptions DefaultProvider() => new()
    {
        BaseUrl = "https://sameday.example/",
        StatusPath = "/api/awb/{awb}/status",
        RequestTimeoutSeconds = 5,
        MaxRetries = 2,
        RetryBaseDelayMilliseconds = 100
    };

    private static (SamedayCourierClient client, RecordingHandler handler) BuildClient(CourierProviderOptions provider)
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler);
        var optionsMonitor = Mock.Of<IOptionsMonitor<CourierIntegrationOptions>>(
            m => m.CurrentValue == BuildOptions(provider));
        var client = new SamedayCourierClient(httpClient, optionsMonitor, NullLogger<SamedayCourierClient>.Instance);
        return (client, handler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetStatusAsync_BlankAwb_ReturnsNull(string? awb)
    {
        var (client, handler) = BuildClient(DefaultProvider());

        var result = await client.GetStatusAsync(awb!);

        result.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_MissingBaseUrl_ReturnsNull()
    {
        var provider = DefaultProvider();
        provider.BaseUrl = string.Empty;
        var (client, handler) = BuildClient(provider);

        var result = await client.GetStatusAsync("AWB1");

        result.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_SuccessfulResponse_ParsesStatusAndEventTime()
    {
        var provider = DefaultProvider();
        var (client, handler) = BuildClient(provider);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"in_transit\",\"eventTime\":\"2026-04-10T08:30:00+00:00\"}",
                Encoding.UTF8,
                "application/json")
        });

        var result = await client.GetStatusAsync("AWB 42");

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("in_transit");
        result.EventTime.Should().Be(DateTimeOffset.Parse("2026-04-10T08:30:00+00:00"));

        handler.Requests.Should().HaveCount(1);
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsoluteUri.Should().Be("https://sameday.example/api/awb/AWB%2042/status");
    }

    [Fact]
    public async Task GetStatusAsync_WithApiKey_AddsApiKeyHeader()
    {
        var provider = DefaultProvider();
        provider.ApiKey = "secret-key";
        provider.ApiKeyHeaderName = "X-Custom-Key";
        var (client, handler) = BuildClient(provider);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"delivered\"}", Encoding.UTF8, "application/json")
        });

        await client.GetStatusAsync("AWB1");

        handler.Requests[0].Headers.TryGetValues("X-Custom-Key", out var values).Should().BeTrue();
        values!.Single().Should().Be("secret-key");
    }

    [Fact]
    public async Task GetStatusAsync_WithBearerToken_AddsAuthorizationHeader()
    {
        var provider = DefaultProvider();
        provider.BearerToken = "token-abc";
        var (client, handler) = BuildClient(provider);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"delivered\"}", Encoding.UTF8, "application/json")
        });

        await client.GetStatusAsync("AWB1");

        var auth = handler.Requests[0].Headers.Authorization;
        auth.Should().NotBeNull();
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("token-abc");
    }

    [Fact]
    public async Task GetStatusAsync_NonRetryable4xx_ReturnsNullWithoutRetry()
    {
        var (client, handler) = BuildClient(DefaultProvider());
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad")
        });

        var result = await client.GetStatusAsync("AWB1");

        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetStatusAsync_TransientServerError_RetriesThenSucceeds()
    {
        var (client, handler) = BuildClient(DefaultProvider());
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"delivered\"}", Encoding.UTF8, "application/json")
        });

        var result = await client.GetStatusAsync("AWB1");

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("delivered");
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetStatusAsync_TooManyRequests_IsRetried()
    {
        var provider = DefaultProvider();
        provider.MaxRetries = 1;
        var (client, handler) = BuildClient(provider);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"pending\"}", Encoding.UTF8, "application/json")
        });

        var result = await client.GetStatusAsync("AWB1");

        result!.ExternalStatus.Should().Be("pending");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStatusAsync_ExhaustsRetries_ReturnsNull()
    {
        var provider = DefaultProvider();
        provider.MaxRetries = 1;
        var (client, handler) = BuildClient(provider);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadGateway));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadGateway));

        var result = await client.GetStatusAsync("AWB1");

        result.Should().BeNull();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStatusAsync_HttpRequestException_IsRetried()
    {
        var provider = DefaultProvider();
        provider.MaxRetries = 1;
        var (client, handler) = BuildClient(provider);
        handler.EnqueueException(new HttpRequestException("connection reset"));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"delivered\"}", Encoding.UTF8, "application/json")
        });

        var result = await client.GetStatusAsync("AWB1");

        result!.ExternalStatus.Should().Be("delivered");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStatusAsync_SuccessWithoutStatusField_ReturnsNull()
    {
        var (client, handler) = BuildClient(DefaultProvider());
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"foo\":\"bar\"}", Encoding.UTF8, "application/json")
        });

        var result = await client.GetStatusAsync("AWB1");

        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetStatusAsync_ExternalCancellation_DoesNotRetry()
    {
        var (client, handler) = BuildClient(DefaultProvider());
        handler.EnqueueException(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await client.GetStatusAsync("AWB1", cts.Token);

        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public void Subclasses_ExposeExpectedCourierCodes()
    {
        var optionsMonitor = Mock.Of<IOptionsMonitor<CourierIntegrationOptions>>(
            m => m.CurrentValue == new CourierIntegrationOptions());
        using var http = new HttpClient(new RecordingHandler());

        new SamedayCourierClient(http, optionsMonitor, NullLogger<SamedayCourierClient>.Instance)
            .CourierCode.Should().Be("SAMEDAY");
        new FanCourierClient(http, optionsMonitor, NullLogger<FanCourierClient>.Instance)
            .CourierCode.Should().Be("FAN");
        new CargusCourierClient(http, optionsMonitor, NullLogger<CargusCourierClient>.Instance)
            .CourierCode.Should().Be("CARGUS");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(() => response);

        public void EnqueueException(Exception exception) => _responses.Enqueue(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more responses queued.");
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
