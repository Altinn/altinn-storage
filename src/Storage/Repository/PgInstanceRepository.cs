using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IInstanceRepository"/>.
    /// </summary>
    public class PgInstanceRepository: IInstanceRepository, IHostedService
    {
        private static readonly string _deleteSql = "call storage.deleteInstance ($1)"; // "delete from storage.instances where alternateId = $1;";
        private static readonly string _insertSql = "call storage.insertInstance ($1, $2, $3)"; // "insert into storage.instances(partyId, alternateId, instance) VALUES ($1, $2, $3)";
        private static readonly string _upsertSql = "call storage.upsertInstance ($1, $2, $3)"; // _insertSql + " on conflict(alternateId) do update set instance = $3";
        private static readonly string _readSql = "select * from storage.readInstance ($1)";
        ////private static readonly string _readSql = $"select i.id, i.instance, d.element " +
        ////    $"from storage.instances i left join storage.dataelements d on i.id = d.instanceInternalId " +
        ////    $"where i.alternateId = $1 " +
        ////    $"order by d.id";

        private readonly ILogger<PgInstanceRepository> _logger;
        private readonly JsonSerializerOptions _options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgInstanceRepository(
            ILogger<PgInstanceRepository> logger,
            NpgsqlDataSource dataSource)
        {
            _logger = logger;
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<Instance> Create(Instance instance)
        {
            Instance updatedInstance = await Upsert(instance, true);
            updatedInstance.Data = new List<DataElement>();
            return updatedInstance;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(Instance item)
        {
            ToInternal(item);
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(item.Id));

            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<InstanceQueryResponse> GetInstancesFromQuery(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            // Postponed some days because the parameter handling is complicated and not very important to the PoC
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid)
        {
            Instance instance = null;
            long internalId = 0;

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                bool instanceCreated = false;
                while (await reader.ReadAsync())
                {
                    if (!instanceCreated)
                    {
                        instanceCreated = true;
                        instance = JsonSerializer.Deserialize<Instance>(reader.GetFieldValue<string>("instance"));
                        internalId = reader.GetFieldValue<long>("id");
                        instance.Data = new();
                    }

                    if (reader["element"] is string elementJson)
                    {
                        instance.Data.Add(JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element")));
                    }
                }

                if (instance == null)
                {
                    return (null, 0);
                }

                SetReadStatus(instance);
                (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
                instance.LastChanged = lastChanged;
                instance.LastChangedBy = lastChangedBy;
            }

            return (ToExternal(instance), internalId);
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance item)
        {
            List<DataElement> dataElements = item.Data;
            Instance updatedInstance = await Upsert(item, false);
            updatedInstance.Data = dataElements;
            return updatedInstance;
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

        private static void SetReadStatus(Instance instance)
        {
            if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Any(d => !d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.UpdatedSinceLastReview;
            }
            else if (instance.Status.ReadStatus == ReadStatus.Read && !instance.Data.Any(d => d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.Unread;
            }
        }

        private async Task<Instance> Upsert(Instance instance, bool insertOnly)
        {
            instance.Id ??= Guid.NewGuid().ToString();
            ToInternal(instance);
            instance.Data = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(insertOnly ? _insertSql : _upsertSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, long.Parse(instance.InstanceOwner.PartyId));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instance.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(instance, _options));

            await pgcom.ExecuteNonQueryAsync();

            return ToExternal(instance);
        }

        private static Instance ToInternal(Instance instance)
        {
            if (instance.Id.Contains('/', StringComparison.Ordinal))
            {
                instance.Id = instance.Id.Split('/')[1];
            }

            return instance;
        }

        private static Instance ToExternal(Instance instance)
        {
            if (!instance.Id.Contains('/', StringComparison.Ordinal))
            {
                instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";
            }

            return instance;
        }
    }
}
