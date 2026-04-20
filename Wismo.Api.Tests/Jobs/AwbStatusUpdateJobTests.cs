using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Quartz;
using Wismo.Api.Couriers;
using Wismo.Api.Jobs;

namespace Wismo.Api.Tests.Jobs;

public class AwbStatusUpdateJobTests
{
    private readonly Mock<IAwbStatusSyncService> _syncService = new();
    private readonly Mock<ILogger<AwbStatusUpdateJob>> _logger = new();
    private readonly AwbStatusUpdateJob _sut;

    public AwbStatusUpdateJobTests()
    {
        _sut = new AwbStatusUpdateJob(_syncService.Object, _logger.Object);
    }

    [Fact]
    public async Task Execute_OnSuccess_InvokesSyncServiceWithContextCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var context = CreateContext(cts.Token);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(cts.Token))
            .ReturnsAsync(5);

        await _sut.Execute(context.Object);

        _syncService.Verify(x => x.SyncInTransitStatusesAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task Execute_OnSuccess_DoesNotThrow()
    {
        var context = CreateContext(CancellationToken.None);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var act = () => _sut.Execute(context.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Execute_WhenCancellationRequested_RethrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext(cts.Token);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.Execute(context.Object);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Execute_WhenOperationCanceledThrownButNoCancellationRequested_WrapsInJobExecutionException()
    {
        var context = CreateContext(CancellationToken.None);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _sut.Execute(context.Object);

        var ex = await act.Should().ThrowAsync<JobExecutionException>();
        ex.Which.RefireImmediately.Should().BeFalse();
        ex.Which.InnerException.Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public async Task Execute_WhenSyncThrowsGenericException_WrapsInJobExecutionExceptionWithoutRefire()
    {
        var context = CreateContext(CancellationToken.None);
        var inner = new InvalidOperationException("boom");
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        var act = () => _sut.Execute(context.Object);

        var ex = await act.Should().ThrowAsync<JobExecutionException>();
        ex.Which.RefireImmediately.Should().BeFalse();
        ex.Which.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task Execute_WhenSyncThrows_LogsError()
    {
        var context = CreateContext(CancellationToken.None);
        var inner = new InvalidOperationException("boom");
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        await Assert.ThrowsAsync<JobExecutionException>(() => _sut.Execute(context.Object));

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                inner,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_OnSuccess_LogsStartAndCompletion()
    {
        var context = CreateContext(CancellationToken.None);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        await _sut.Execute(context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_WhenCancellationRequested_LogsWarning()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext(cts.Token);
        _syncService
            .Setup(x => x.SyncInTransitStatusesAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.Execute(context.Object));

        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static Mock<IJobExecutionContext> CreateContext(CancellationToken cancellationToken)
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(x => x.CancellationToken).Returns(cancellationToken);
        context.SetupGet(x => x.FireTimeUtc).Returns(DateTimeOffset.UtcNow);
        context.SetupGet(x => x.ScheduledFireTimeUtc).Returns(DateTimeOffset.UtcNow);
        context.SetupGet(x => x.NextFireTimeUtc).Returns(DateTimeOffset.UtcNow.AddMinutes(5));
        return context;
    }
}
