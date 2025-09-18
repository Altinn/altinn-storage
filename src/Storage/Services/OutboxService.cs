using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Background service responsible for processing outbox messages in a loop
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="OutboxService"/> class.
    /// </remarks>
    /// <param name="logger">The logger to use for logging information.</param>
    /// <param name="serviceProvider">The service provider to resolve dependencies.</param>
    /// <param name="wolverineSettings">Wolverine settings</param>
    public class OutboxService(ILogger<OutboxService> logger, IServiceProvider serviceProvider, IOptions<WolverineSettings> wolverineSettings) : BackgroundService
    {
        private readonly ILogger<OutboxService> _logger = logger;
        private readonly WolverineSettings _wolverineSettings = wolverineSettings.Value;
        private readonly Guid _podId = Guid.NewGuid();
        private readonly SortedSet<string> _instancesWithEvent = [];
        private static readonly ActivitySource _activitySource = new(nameof(OutboxService));

        /// <summary>
        /// Executes the background service logic.
        /// </summary>
        /// <param name="stoppingToken">Token to signal cancellation.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_wolverineSettings.EnableCustomOutbox)
            {
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime leaseExpiry = DateTime.UtcNow.AddSeconds(_wolverineSettings.LeaseSecs);
                if (!await outbox.TryAcquireLeaseAsync("outbox", _podId, leaseExpiry))
                {
                    await Task.Delay(TimeSpan.FromSeconds(_wolverineSettings.TryGettingPollMasterIntervalSecs), stoppingToken);
                }
                else
                {
                    _logger.LogInformation("OutboxService with id {PodId} got lease", _podId);
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
                            await Task.Delay(TimeSpan.FromMilliseconds(_wolverineSettings.PollErrorDelayMs), stoppingToken);
                        }

                        await PublishAndDeletePolledMessages(messageBus, outbox, dps, stoppingToken);

                        if (dps.Count < _wolverineSettings.PollMaxSize && !stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(_wolverineSettings.PollIdleTimeMs, stoppingToken);
                        }

                        if (DateTime.UtcNow > leaseExpiry.AddSeconds(-_wolverineSettings.LeaseSecs * 0.8))
                        {
                            leaseExpiry = DateTime.UtcNow.AddSeconds(_wolverineSettings.LeaseSecs);
                            if (!await outbox.RenewLeaseAsync("outbox", _podId, leaseExpiry))
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private async Task PublishAndDeletePolledMessages(IMessageBus messageBus, IOutboxRepository outbox, List<SyncInstanceToDialogportenCommand> dps, CancellationToken stoppingToken)
        {
            // TODO: Consider whether to do all deletes in a single operation. This will improve
            // performance, but complicates error handling and logging.
            foreach (var dp in dps)
            {
                if (_instancesWithEvent.Contains(dp.InstanceId))
                {
                    _logger.LogInformation("Outbox skipping instance {InstanceId} as it is already being processed", dp.InstanceId);
                    continue;
                }
                else
                {
                    _instancesWithEvent.Add(dp.InstanceId);
                }

                bool published = false;
                try
                {
                    using (Activity activity = _activitySource.StartActivity("PublishToASB"))
                    {
                        await messageBus.PublishAsync(dp);
                        _logger.LogInformation("Outbox published instance {InstanceId} to ASB, event {Event}, createdAt {CreatedAt}", dp.InstanceId, dp.EventType, dp.InstanceCreatedAt);
                        published = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox push to ASB");
                }

                if (published)
                {
                    try
                    {
                        await outbox.Delete(Guid.Parse(dp.InstanceId));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Outbox delete");
                        await Task.Delay(TimeSpan.FromMilliseconds(_wolverineSettings.PollErrorDelayMs), stoppingToken);
                    }
                }
            }
        }

        /// <summary>
        /// Stops the background service.
        /// </summary>
        /// <param name="cancellationToken">Token to signal cancellation.</param>
        /// <remarks>
        /// Release the lease
        /// </remarks>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            await outbox.ReleaseLeaseAsync("outbox", _podId);
            _logger.LogInformation("OutboxService with id {PodId} is shutting down", _podId);
        }
    }
}
