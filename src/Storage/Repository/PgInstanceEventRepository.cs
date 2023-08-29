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
    public class PgInstanceEventRepository: IInstanceEventRepository
    {
        private readonly string _readSql = "select * from storage.readinstanceevent($1)";
        private readonly string _deleteSql = "call storage.deleteInstanceevent($1)";
        private readonly string _insertSql = "call storage.insertInstanceevent($1, $2, $3)";
        private readonly string _filterSql = "select * from storage.filterinstanceevent($1, $2, $3, $4)";

        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceEventRepository"/> class.
        /// </summary>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgInstanceEventRepository(NpgsqlDataSource dataSource)
        {
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
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<InstanceEvent>("event");
            }

            return null;
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
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, eventTypes == null || eventTypes.Length == 0 ? (object)DBNull.Value : eventTypes);

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
    }
}
