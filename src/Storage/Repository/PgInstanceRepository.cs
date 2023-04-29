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
    public class PgInstanceRepository: IInstanceRepository, IHostedService
    {
        private static readonly string _deleteSql = "delete from storage.instances where alternateId = $1;";
        private static readonly string _insertSql = "insert into storage.instances(partyId, alternateId, instance) VALUES ($1, $2, $3)";
        private static readonly string _upsertSql = _insertSql + " on conflict(alternateId) do update set instance = $3";
        private static readonly string _readSql = $"select i.id, i.instance, d.element " +
            $"from storage.instances i left join storage.dataelements d on i.id = d.instanceInternalId " +
            $"where i.alternateId = $1 " +
            $"order by d.id";

        private readonly string _connectionString;
        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ILogger<PgDataRepository> _logger;
        private readonly JsonSerializerOptions _options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceRepository"/> class.
        /// </summary>
        /// <param name="postgresSettings">DB params.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        public PgInstanceRepository(
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
            NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();
            NpgsqlCommand pgcom = new(_deleteSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(item.Id), NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<InstanceQueryResponse> GetInstancesFromQuery(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            throw new NotImplementedException();

            //InstanceQueryResponse queryResponse = new InstanceQueryResponse
            //{
            //    Count = 0,
            //    Instances = new List<Instance>()
            //};

            //while (queryResponse.Count < size)
            //{
            //    QueryRequestOptions options = new QueryRequestOptions() { MaxBufferedItemCount = 0, MaxConcurrency = -1, MaxItemCount = size - queryResponse.Count, ResponseContinuationTokenLimitInKb = 7 };

            //    string tokenValue = string.IsNullOrEmpty(continuationToken) ? null : continuationToken;
            //    IQueryable<Instance> queryBuilder = Container.GetItemLinqQueryable<Instance>(requestOptions: options, continuationToken: tokenValue);

            //    try
            //    {
            //        queryBuilder = BuildQueryFromParameters(queryParams, queryBuilder, options);
            //    }
            //    catch (Exception e)
            //    {
            //        queryResponse.Exception = e.Message;
            //        return queryResponse;
            //    }

            //    try
            //    {
            //        var iterator = queryBuilder.ToFeedIterator();

            //        FeedResponse<Instance> feedResponse = await iterator.ReadNextAsync();

            //        if (feedResponse.Count == 0 && !iterator.HasMoreResults)
            //        {
            //            queryResponse.ContinuationToken = string.Empty;
            //            break;
            //        }

            //        List<Instance> instances = feedResponse.ToList();
            //        await PostProcess(instances);
            //        queryResponse.Instances.AddRange(instances);
            //        queryResponse.Count += instances.Count;

            //        if (string.IsNullOrEmpty(feedResponse.ContinuationToken))
            //        {
            //            queryResponse.ContinuationToken = string.Empty;
            //            break;
            //        }

            //        queryResponse.ContinuationToken = feedResponse.ContinuationToken;
            //        continuationToken = feedResponse.ContinuationToken;
            //    }
            //    catch (Exception e)
            //    {
            //        _logger.LogError(e, "Exception querying CosmosDB for instances");
            //        queryResponse.Exception = e.Message;
            //        break;
            //    }
            //}

            //return queryResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid)
        {
            Instance instance = null;
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();
            long internalId = 0;

            await using NpgsqlCommand pgcom = new(_readSql, conn)
            {
                Parameters =
                {
                    new() { Value = instanceGuid, NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
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

                    string elementJson = reader["element"] as string;
                    if (elementJson != null)
                    {
                        instance.Data.Add(JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element")));
                    }
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
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(insertOnly ? _insertSql : _upsertSql, conn)
            {
                Parameters =
                {
                    new() { Value = long.Parse(instance.InstanceOwner.PartyId), NpgsqlDbType = NpgsqlDbType.Bigint },
                    new() { Value = new Guid(instance.Id), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = JsonSerializer.Serialize(instance, _options), NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.ExecuteNonQueryAsync();

            return ToExternal(instance);
        }

        private Instance ToInternal(Instance instance)
        {
            if (instance.Id.Contains('/', StringComparison.Ordinal))
            {
                instance.Id = instance.Id.Split('/')[1];
            }

            return instance;
        }

        private Instance ToExternal(Instance instance)
        {
            if (!instance.Id.Contains('/', StringComparison.Ordinal))
            {
                instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";
            }

            return instance;
        }
    }
}
