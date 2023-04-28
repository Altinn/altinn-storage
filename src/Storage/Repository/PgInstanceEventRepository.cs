using System.Collections.Generic;
using System.Linq;
using System.Net;
using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Data;
using System.Threading;

namespace Altinn.Platform.Storage.Repository
{
    public class PgInstanceEventRepository: IInstanceEventRepository, IHostedService
    {
        private readonly string _insertSql = "insert into storage.instanceEvents(instance, alternateId, event) VALUES ($1, $2, $3);";
        private readonly string _readAllSql = "select instance, event from storage.instanceEvents where instance = $1;";
        private readonly string _filterSql = "select instance, event from storage.instanceEvents" +
            " where instance = $1 and (event->>'Created')::timestamp >= $2 and (event->>'Created')::timestamp <= $3 and ($4 is null or event->>'EventType' ilike any ($4));";

        private readonly string _readSql = "select instance, event from storage.instanceEvents where alternateId = $1;";
        private readonly string _deleteSql = "delete from storage.instanceEvents where instance = $1;";
        private readonly string _updateSql = "update storage.instanceEvents set event = $2 where alternateId = $1;";

        private readonly string _connectionString;
        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ILogger<PgDataRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceEventRepository"/> class.
        /// </summary>
        /// <param name="postgresSettings">DB params.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        public PgInstanceEventRepository(
            IOptions<PostgreSqlSettings> postgresSettings,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<PgDataRepository> logger)
        {
            _connectionString =
                string.Format(postgresSettings.Value.ConnectionString, postgresSettings.Value.StorageDbPwd);
            _storageConfiguration = storageConfiguration.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> InsertInstanceEvent(InstanceEvent instanceEvent)
        {
            instanceEvent.Id ??= Guid.NewGuid();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_insertSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(instanceEvent.InstanceId.Split('/').Last()), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = instanceEvent.Id, NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = JsonSerializer.Serialize(instanceEvent), NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.ExecuteNonQueryAsync();

            return instanceEvent;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> GetOneEvent(string instanceId, Guid eventGuid)
        {
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_readSql, conn)
            {
                Parameters =
                {
                    new() { Value = eventGuid, NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                return JsonSerializer.Deserialize<InstanceEvent>(reader.GetFieldValue<string>("event"));
            }
        }

        /// <inheritdoc/>
        public async Task<List<InstanceEvent>> ListInstanceEvents(
            string instanceId,
            string[] eventTypes,
            DateTime? fromDateTime,
            DateTime? toDateTime)
        {
            List<InstanceEvent> events = new();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_filterSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(instanceId.Split('/').Last()), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = fromDateTime ?? DateTime.MinValue, NpgsqlDbType = NpgsqlDbType.Timestamp },
                    new() { Value = toDateTime ?? DateTime.MaxValue, NpgsqlDbType = NpgsqlDbType.Timestamp },
                    new() { Value = eventTypes ?? (object)DBNull.Value, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text },
                },
            };
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    events.Add(JsonSerializer.Deserialize<InstanceEvent>(reader.GetFieldValue<string>("event")));
                }
            }

            return events;
        }

        /// <inheritdoc/>
        public async Task<int> DeleteAllInstanceEvents(string instanceId)
        {
            NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();
            NpgsqlCommand pgcom = new(_deleteSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(instanceId.Split('/').Last()), NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
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
