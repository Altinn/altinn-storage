using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.ApplicationInsights;
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
    private readonly TelemetryClient _telemetryClient;
    private readonly IInstanceRepository _instanceRepository;

    private readonly string _insertInstanceEventsSql = "call storage.insertinstanceevents($1, $2)";

    /// <summary>
    /// Initializes a new instance of the <see cref="PgInstanceAndEventsRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger to use when writing to logs.</param>
    /// <param name="dataSource">The npgsql data source.</param>
    /// <param name="instanceRepository">Instance repo</param>
    /// <param name="telemetryClient">Telemetry client</param>
    public PgInstanceAndEventsRepository(
        ILogger<PgInstanceAndEventsRepository> logger,
        NpgsqlDataSource dataSource,
        IInstanceRepository instanceRepository,
        TelemetryClient telemetryClient = null)
    {
        _logger = logger;
        _dataSource = dataSource;
        _instanceRepository = instanceRepository;
        _telemetryClient = telemetryClient;
    }

    /// <inheritdoc/>
    public async Task<Instance> Update(Instance instance, List<string> updateProperties, List<InstanceEvent> events)
    {
        if (events.Count == 0)
        {
            return await _instanceRepository.Update(instance, updateProperties);
        }

        foreach (var instanceEvent in events)
        {
            instanceEvent.Id ??= Guid.NewGuid();
        }
        
        // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
        instance.LastChanged = instance.LastChanged != null ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc) : null;
        List<DataElement> dataElements = instance.Data;

        PgInstanceRepository.ToInternal(instance);
        instance.Data = null;
        await using NpgsqlBatch batch = _dataSource.CreateBatch();

        NpgsqlBatchCommand updateCommand = new(PgInstanceRepository.UpdateSql);
        PgInstanceRepository.BuildUpdateCommand(instance, updateProperties, updateCommand.Parameters);
        batch.BatchCommands.Add(updateCommand);

        NpgsqlBatchCommand insertEventsComand = new(_insertInstanceEventsSql);
        insertEventsComand.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instance.Id.Split('/').Last()));
        insertEventsComand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, events);
        batch.BatchCommands.Add(insertEventsComand);

        await using NpgsqlDataReader reader = await batch.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            instance = await reader.GetFieldValueAsync<Instance>("updatedInstance");
        }

        instance.Data = dataElements; // TODO: requery instead?

        return PgInstanceRepository.ToExternal(instance);
    }
}
