using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IInstanceEventRepository"/>.
    /// </summary>
    public class PgInstanceEventRepository: IInstanceEventRepository, IHostedService
    {
        private readonly string _readSql = "select * from storage.readInstanceEvent($1)"; //"select event from storage.instanceEvents where alternateId = $1;";
        private readonly string _deleteSql = "call storage.deleteInstanceEvent($1)"; //"delete from storage.instanceEvents where instance = $1;";
        private readonly string _insertSql = "call storage.insertInstanceEvent($1, $2, $3)"; // "insert into storage.instanceEvents(instance, alternateId, event) VALUES ($1, $2, $3);";
        private readonly string _filterSql = "select * from storage.filterInstanceEvent($1, $2, $3, $4)";
        ////private readonly string _filterSql = "select event from storage.instanceEvents" +
        ////    " where instance = $1 and (event->>'Created')::timestamp >= $2 and (event->>'Created')::timestamp <= $3 and ($4 is null or event->>'EventType' ilike any ($4));";

        private readonly ILogger<PgInstanceEventRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceEventRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgInstanceEventRepository(
            ILogger<PgInstanceEventRepository> logger,
            NpgsqlDataSource dataSource)
        {
            _logger = logger;
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> InsertInstanceEvent(InstanceEvent instanceEvent)
        {
            instanceEvent.Id ??= Guid.NewGuid();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceEvent.InstanceId.Split('/').Last()));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceEvent.Id);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instanceEvent);

            await pgcom.ExecuteNonQueryAsync();

            return instanceEvent;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> GetOneEvent(string instanceId, Guid eventGuid)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, eventGuid);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            await reader.ReadAsync();
            return reader.GetFieldValue<InstanceEvent>("event");
        }

        /// <inheritdoc/>
        public async Task<List<InstanceEvent>> ListInstanceEvents(
            string instanceId,
            string[] eventTypes,
            DateTime? fromDateTime,
            DateTime? toDateTime)
        {
            List<InstanceEvent> events = new();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_filterSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId.Split('/').Last()));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Timestamp, fromDateTime ?? DateTime.MinValue);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Timestamp, toDateTime ?? DateTime.MaxValue);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, eventTypes ?? (object)DBNull.Value);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    events.Add(reader.GetFieldValue<InstanceEvent>("event"));
                }
            }

            return events;
        }

        /// <inheritdoc/>
        public async Task<int> DeleteAllInstanceEvents(string instanceId)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId.Split('/').Last()));

            return await pgcom.ExecuteNonQueryAsync();
        }

        /// <inheritdoc/>
        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
