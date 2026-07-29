#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Background service responsible for processing outbox messages in a loop
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OutboxService"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging information.</param>
/// <param name="serviceProvider">The service provider to resolve dependencies.</param>
/// <param name="wolverineSettings">Wolverine settings</param>
public class OutboxService(
    ILogger<OutboxService> logger,
    IServiceProvider serviceProvider,
    IOptions<WolverineSettings> wolverineSettings
) : BackgroundService
{
    private const string _outboxResource = "outbox";
    private readonly ILogger<OutboxService> _logger = logger;
    private readonly WolverineSettings _wolverineSettings = wolverineSettings.Value;
    private readonly Guid _podId = Guid.NewGuid();

    /// <summary>
    /// Executes the background service logic.
    /// </summary>
    /// <param name="stoppingToken">Token to signal cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_wolverineSettings.EnableSending)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = serviceProvider.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            DateTime leaseExpiry = DateTime.UtcNow.AddSeconds(_wolverineSettings.LeaseSecs);
            if (await outbox.TryAcquireLeaseAsync(_outboxResource, _podId, leaseExpiry))
            {
                var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await PollWhileHoldingLease(messageBus, outbox, leaseExpiry, stoppingToken);
            }
            else
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_wolverineSettings.TryGettingPollMasterIntervalSecs),
                    stoppingToken
                );
            }
        }
    }

    private async Task PollWhileHoldingLease(
        IMessageBus messageBus,
        IOutboxRepository outbox,
        DateTime leaseExpiry,
        CancellationToken stoppingToken
    )
    {
        _logger.LogInformation("OutboxService with id {PodId} got lease", _podId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<SyncInstanceToDialogportenCommand> dps = [];
                try
                {
                    dps = await outbox.Poll(_wolverineSettings.PollMaxSize);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox polling");
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_wolverineSettings.PollErrorDelayMs),
                        stoppingToken
                    );
                }

                await PublishAndDeletePolledMessages(messageBus, outbox, dps, stoppingToken);

                if (
                    dps.Count < _wolverineSettings.PollMaxSize
                    && !stoppingToken.IsCancellationRequested
                )
                {
                    await Task.Delay(_wolverineSettings.PollIdleTimeMs, stoppingToken);
                }

                if (DateTime.UtcNow > leaseExpiry.AddSeconds(-_wolverineSettings.LeaseSecs * 0.2))
                {
                    leaseExpiry = DateTime.UtcNow.AddSeconds(_wolverineSettings.LeaseSecs);
                    if (!await outbox.RenewLeaseAsync(_outboxResource, _podId, leaseExpiry))
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            // Holder scoped: a no-op if the lease was already lost, and releasing one we still
            // hold saves the next pod waiting for it to expire.
            await outbox.ReleaseLeaseAsync(_outboxResource, _podId);
            _logger.LogInformation("OutboxService with id {PodId} released lease", _podId);
        }
    }

    private async Task PublishAndDeletePolledMessages(
        IMessageBus messageBus,
        IOutboxRepository outbox,
        List<SyncInstanceToDialogportenCommand> dps,
        CancellationToken stoppingToken
    )
    {
        // TODO: Consider whether to do all deletes in a single operation. This will improve
        // performance, but complicates error handling and logging.
        foreach (var dp in dps)
        {
            bool published = false;
            try
            {
                await messageBus.PublishAsync(dp);
                _logger.LogInformation(
                    "Outbox published instance {InstanceId} to ASB, event {Event}, createdAt {CreatedAt}",
                    dp.InstanceId,
                    dp.EventType,
                    dp.InstanceCreatedAt
                );
                published = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox push to ASB for instance {InstanceId}", dp.InstanceId);
                await Task.Delay(
                    TimeSpan.FromMilliseconds(_wolverineSettings.PollErrorDelayMs),
                    stoppingToken
                );
            }

            if (published)
            {
                try
                {
                    await outbox.Delete(Guid.Parse(dp.InstanceId));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox delete for instance {InstanceId}", dp.InstanceId);
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_wolverineSettings.PollErrorDelayMs),
                        stoppingToken
                    );
                }
            }
        }
    }
}
