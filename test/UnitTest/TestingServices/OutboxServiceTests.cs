using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class OutboxServiceTests
{
    [Fact]
    public async Task StopAsync_LeaseNeverAcquired_DoesNotReleaseLease()
    {
        var leasePollStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var outbox = new Mock<IOutboxRepository>(MockBehavior.Strict);
        outbox
            .Setup(repository =>
                repository.TryAcquireLeaseAsync("outbox", It.IsAny<Guid>(), It.IsAny<DateTime>())
            )
            .Callback(() => leasePollStarted.TrySetResult())
            .ReturnsAsync(false);

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
            Times.Never
        );
    }

    [Fact]
    public async Task StopAsync_LeaseAcquired_CancelsBackgroundExecutionAndReleasesLease()
    {
        var pollStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var outbox = new Mock<IOutboxRepository>(MockBehavior.Strict);
        outbox
            .Setup(repository =>
                repository.TryAcquireLeaseAsync("outbox", It.IsAny<Guid>(), It.IsAny<DateTime>())
            )
            .ReturnsAsync(true);
        outbox
            .Setup(repository => repository.Poll(It.IsAny<int>()))
            .Callback(() => pollStarted.TrySetResult())
            .ReturnsAsync(new List<SyncInstanceToDialogportenCommand>());
        outbox
            .Setup(repository => repository.ReleaseLeaseAsync("outbox", It.IsAny<Guid>()))
            .ReturnsAsync(true);

        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton(outbox.Object)
            .AddSingleton(new Mock<IMessageBus>().Object)
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

                    // Long enough that the polling loop parks in the idle delay, so the stop below
                    // has to cancel it.
                    PollIdleTimeMs = 60_000,
                }
            )
        );

        await service.StartAsync(CancellationToken.None);
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompleted);
        outbox.Verify(
            repository => repository.ReleaseLeaseAsync("outbox", It.IsAny<Guid>()),
            Times.Once
        );
    }
}
