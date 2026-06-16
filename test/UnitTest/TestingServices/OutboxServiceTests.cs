using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class OutboxServiceTests
{
    [Fact]
    public async Task StopAsync_CancelsBackgroundExecutionAndReleasesLease()
    {
        var leasePollStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var outbox = new Mock<IOutboxRepository>(MockBehavior.Strict);
        outbox
            .Setup(repository =>
                repository.TryAcquireLeaseAsync("outbox", It.IsAny<Guid>(), It.IsAny<DateTime>())
            )
            .Callback(() => leasePollStarted.SetResult())
            .ReturnsAsync(false);
        outbox
            .Setup(repository => repository.ReleaseLeaseAsync("outbox", It.IsAny<Guid>()))
            .ReturnsAsync(true);

        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton(outbox.Object)
            .BuildServiceProvider();

        var service = new OutboxService(
            NullLogger<OutboxService>.Instance,
            services,
            Options.Create(
                new WolverineSettings
                {
                    EnableSending = true,
                    TryGettingPollMasterIntervalSecs = 60,
                    LeaseSecs = 60,
                }
            )
        );

        await service.StartAsync(CancellationToken.None);
        await leasePollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompleted);
        outbox.Verify(
            repository => repository.ReleaseLeaseAsync("outbox", It.IsAny<Guid>()),
            Times.Once
        );
    }
}
