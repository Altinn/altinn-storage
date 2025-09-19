using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IInstanceEventRepository"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="PgInstanceEventRepository"/> class.
    /// </remarks>
    /// <param name="dataSource">The npgsql data source.</param>
    /// <param name="outboxRepository">The outbox repository</param>
    public class PgInstanceEventRepository(
        NpgsqlDataSource dataSource,
        IOutboxRepository outboxRepository) : IInstanceEventRepository
    {
        private readonly string _readSql = "select * from storage.readinstanceevent($1)";
        private readonly string _deleteSql = "select * from storage.deleteInstanceevent($1)";
        private readonly string _insertSql = "call storage.insertInstanceevent($1, $2, $3)";
        private readonly string _filterSql = "select * from storage.filterinstanceevent($1, $2, $3, $4)";

        private readonly NpgsqlDataSource _dataSource = dataSource;

        /// <inheritdoc/>
        public async Task<InstanceEvent> InsertInstanceEvent(InstanceEvent instanceEvent, Instance instance = null)
        {
            instanceEvent.Id ??= Guid.NewGuid();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceEvent.InstanceId.Split('/')[^1]));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceEvent.Id);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instanceEvent);

            await pgcom.ExecuteNonQueryAsync();

            if (instance != null)
            {
                SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                    instance.AppId,
                    instanceEvent.InstanceOwnerPartyId,
                    instanceEvent.InstanceId.Split('/')[^1],
                    (DateTime)instance.Created,
                    false,
                    Enum.Parse<Interface.Enums.InstanceEventType>(instanceEvent.EventType));
                await outboxRepository.Insert(instanceUpdateCommand);
            }

            return instanceEvent;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> GetOneEvent(string instanceId, Guid eventGuid)
        {
            InstanceEvent instanceEvent = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, eventGuid);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                instanceEvent = await reader.GetFieldValueAsync<InstanceEvent>("event");
            }

            return instanceEvent;
        }

        /// <inheritdoc/>
        public async Task<List<InstanceEvent>> ListInstanceEvents(
            string instanceId,
            string[] eventTypes,
            DateTime? fromDateTime,
            DateTime? toDateTime)
        {
            List<InstanceEvent> events = [];
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_filterSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId.Split('/').Last()));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, fromDateTime ?? DateTime.MinValue);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, toDateTime ?? DateTime.MaxValue);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, eventTypes == null || eventTypes.Length == 0 ? (object)DBNull.Value : eventTypes);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    events.Add(await reader.GetFieldValueAsync<InstanceEvent>("event"));
                }
            }

            return events;
        }

        /// <inheritdoc/>
        public async Task<int> DeleteAllInstanceEvents(string instanceId)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId.Split('/').Last()));

            int rc = (int)await pgcom.ExecuteScalarAsync();
            return rc;
        }
    }
}
