#nullable disable

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IInstanceAndEventsRepository"/>.
/// </summary>
public class PgInstanceAndEventsRepository : IInstanceAndEventsRepository
{
    private readonly ILogger<PgInstanceAndEventsRepository> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IInstanceRepository _instanceRepository;
    private readonly IOutboxRepository _outboxRepository;

    private readonly string _insertInstanceEventsSql = "call storage.insertinstanceevents($1, $2)";

    /// <summary>
    /// Initializes a new instance of the <see cref="PgInstanceAndEventsRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger to use when writing to logs.</param>
    /// <param name="dataSource">The npgsql data source.</param>
    /// <param name="instanceRepository">Instance repo</param>
    /// <param name="outboxRepository">Outbox repo</param>
    public PgInstanceAndEventsRepository(
        ILogger<PgInstanceAndEventsRepository> logger,
        NpgsqlDataSource dataSource,
        IInstanceRepository instanceRepository,
        IOutboxRepository outboxRepository = null
    )
    {
        _logger = logger;
        _dataSource = dataSource;
        _instanceRepository = instanceRepository;
        _outboxRepository = outboxRepository;
    }

    /// <inheritdoc/>
    public async Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        List<InstanceEvent> events,
        CancellationToken cancellationToken
    )
    {
        if (events.Count == 0)
        {
            return await _instanceRepository.Update(instance, updateProperties, cancellationToken);
        }

        foreach (var instanceEvent in events)
        {
            instanceEvent.Id ??= Guid.NewGuid();
        }

        // Align precision with Postgres (microseconds vs DateTime 100ns ticks)
        instance.LastChanged =
            instance.LastChanged != null
                ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc)
                : null;

        InstanceInternal updateResult = null;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var batch = new NpgsqlBatch(connection, tx))
            {
                // Update instance
                var updateCommand = new NpgsqlBatchCommand(PgInstanceRepository.UpdateSql);
                PgInstanceRepository.BuildUpdateCommand(
                    instance,
                    updateProperties,
                    updateCommand.Parameters
                );
                batch.BatchCommands.Add(updateCommand);

                // Insert events
                var insertEventsCommand = new NpgsqlBatchCommand(_insertInstanceEventsSql);
                insertEventsCommand.Parameters.AddWithValue(
                    NpgsqlDbType.Uuid,
                    new Guid(instance.Id)
                );
                insertEventsCommand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, events);
                batch.BatchCommands.Add(insertEventsCommand);

                await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    updateResult = await PgInstanceRepository.ReadUpdatedInstanceAsync(
                        reader,
                        instance.InternalId,
                        cancellationToken
                    );
                }
            }

            if (updateResult is null)
            {
                throw PgInstanceRepository.CreateMissingUpdateResultException(
                    "storage.updateinstance_v4"
                );
            }

            updateResult.Data = instance.Data;

            if (_outboxRepository != null && events.Count > 0)
            {
                InstanceEventType eventType =
                    OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation(events);
                SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                    updateResult.AppId,
                    updateResult.InstanceOwner.PartyId,
                    updateResult.Id,
                    (DateTime)updateResult.Created,
                    false,
                    eventType
                );

                await _outboxRepository.Insert(instanceUpdateCommand, connection, tx);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Failed to update instance {InstanceId} with events (rolled back).",
                instance.Id
            );
            throw;
        }

        return updateResult;
    }
}
