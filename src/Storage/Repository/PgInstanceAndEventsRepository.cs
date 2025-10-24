using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
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
        IOutboxRepository outboxRepository = null)
    {
        _logger = logger;
        _dataSource = dataSource;
        _instanceRepository = instanceRepository;
        _outboxRepository = outboxRepository;
    }

    /// <inheritdoc/>
    public async Task<Instance> Update(Instance instance, List<string> updateProperties, List<InstanceEvent> events, CancellationToken cancellationToken)
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
        instance.LastChanged = instance.LastChanged != null
            ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc)
            : null;

        List<DataElement> dataElements = instance.Data;

        PgInstanceRepository.ToInternal(instance);
        instance.Data = null;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var batch = new NpgsqlBatch(connection, tx))
            {
                // Update instance
                var updateCommand = new NpgsqlBatchCommand(PgInstanceRepository.UpdateSql);
                PgInstanceRepository.BuildUpdateCommand(instance, updateProperties, updateCommand.Parameters);
                batch.BatchCommands.Add(updateCommand);

                // Insert events
                var insertEventsCommand = new NpgsqlBatchCommand(_insertInstanceEventsSql);
                insertEventsCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instance.Id.Split('/')[^1]));
                insertEventsCommand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, events);
                batch.BatchCommands.Add(insertEventsCommand);

                await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    instance = await reader.GetFieldValueAsync<Instance>("updatedInstance", cancellationToken);
                }
            }

            instance.Data = dataElements; // (Optional) Consider re-querying to reflect persisted state.

            if (_outboxRepository != null && events.Count > 0)
            {
                InstanceEvent eventForSync = events.OrderByDescending(e => e.Created).First();
                SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                    instance.AppId,
                    instance.InstanceOwner.PartyId,
                    instance.Id.Split('/')[^1],
                    (DateTime)instance.Created,
                    false,
                    Enum.Parse<Interface.Enums.InstanceEventType>(eventForSync.EventType));

                await _outboxRepository.Insert(instanceUpdateCommand, connection);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Failed to update instance {InstanceId} with events (rolled back).", instance.Id);
            throw;
        }

        return PgInstanceRepository.ToExternal(instance);
    }
}
