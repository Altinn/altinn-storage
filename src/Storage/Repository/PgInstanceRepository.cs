﻿using System;
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
        private static readonly string _readSqlNoElements = "select * from storage.readInstanceNoElements ($1)";
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
            InstanceQueryResponse queryResponse = new() { Count = 0, Instances = new() };
            long continueIdx = string.IsNullOrEmpty(continuationToken) ? -1 : long.Parse(continuationToken);
            int maxIterations = 100_000;
            int currentIteration = 0;
            while (queryResponse.Count < size)
            {
                if (++currentIteration > maxIterations)
                {
                    queryResponse.Exception = "Please narrow the seach parameters. The current search is too slow.";
                    return queryResponse;
                }

                (IQueryable<Instance> queryBuilder, continueIdx) = await GetInstances(size - (int)queryResponse.Count, continueIdx, queryParams);
                try
                {
                    if (queryBuilder.Count() > 0)
                    {
                        queryBuilder = InstanceQueryHelper.BuildQueryFromParameters(queryParams, queryBuilder, new());
                    }
                    else
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    queryResponse.Exception = e.Message;
                    return queryResponse;
                }

                try
                {
                    var instancesThisIteration = queryBuilder.ToList();
                    if (instancesThisIteration.Count > 0)
                    {
                        queryResponse.Instances.AddRange(instancesThisIteration);
                        queryResponse.Count += instancesThisIteration.Count;
                    }

                    if (queryResponse.Count == size || continueIdx < 0)
                    {
                        if (continueIdx >= 0)
                        {
                            queryResponse.ContinuationToken = continueIdx.ToString();
                        }

                        break;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception querying PostgreSQL for instances");
                    queryResponse.Exception = e.Message;
                    break;
                }
            }

            return queryResponse;
        }

        private async Task<(IQueryable<Instance>, long)> GetInstances(int size, long continueIdx, Dictionary<string, StringValues> queryParams)
        {
            // TODO: Add more db predicates. Currently only partyId (and continueation) is supported in the db query. Other predicates are left to InstanceQueryHelper

            Instance instance = null;
            List<Instance> instances = new List<Instance>();
            long id = -1;
            string partyPredicate = null;
            int partyId = 0;
            int[] partyIdArray = null;

            StringValues partyIdStringValues = queryParams.ContainsKey("instanceOwner.partyId") ? queryParams["instanceOwner.partyId"] : default(StringValues);
            if (partyIdStringValues.Count == 1)
            {
                partyId = int.Parse(partyIdStringValues.First());
                partyPredicate = $" AND partyId = $3 ";
            }
            else if (partyIdStringValues.Count > 1)
            {
                partyIdArray = partyIdStringValues.Select(p => int.Parse(p)).ToArray();
                partyPredicate = $" AND partyId = ANY ($3) ";
            }

            string readManySql =
                $"WITH instances AS " +
                $"( " +
                $"    SELECT id, instance FROM storage.instances " +
                $"    WHERE id > $1 " + partyPredicate +
                $"    ORDER BY id " +
                $"    FETCH FIRST $2 ROWS ONLY " +
                $") " +
                $"    SELECT instances.id, instances.instance, element FROM instances LEFT JOIN storage.dataelements d ON instances.id = d.instanceInternalId " +
                $"    ORDER BY instances.id ";

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(readManySql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, continueIdx);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, size);
            if (partyIdStringValues.Count == 1)
            {
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, partyId);
            }
            else if (partyIdStringValues.Count > 1)
            {
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Integer, partyIdArray);
            }

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                long previousId = -1;
                while (await reader.ReadAsync())
                {
                    id = reader.GetFieldValue<long>("id");
                    if (id != previousId)
                    {
                        SetStatuses(instance);
                        instance = JsonSerializer.Deserialize<Instance>(reader.GetFieldValue<string>("instance"));
                        instances.Add(instance);
                        instance.Data = new();
                        previousId = id;
                    }

                    if (reader["element"] is string elementJson)
                    {
                        instance.Data.Add(JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element")));
                    }
                }

                SetStatuses(instance);
            }

            return (instances.AsQueryable(), instances.Count == size ? id : -1);
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid, bool includeElements = true)
        {
            Instance instance = null;
            long internalId = 0;

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(includeElements ? _readSql : _readSqlNoElements);
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

                    if (includeElements && reader["element"] is string elementJson)
                    {
                        instance.Data.Add(JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element")));
                    }
                }

                if (instance == null)
                {
                    return (null, 0);
                }

                SetStatuses(instance);
            }

            return (instance, internalId);
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

        private static void SetStatuses(Instance instance)
        {
            if (instance == null)
            {
                return;
            }

            if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Any(d => !d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.UpdatedSinceLastReview;
            }
            else if (instance.Status.ReadStatus == ReadStatus.Read && !instance.Data.Any(d => d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.Unread;
            }

            (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
            instance.LastChanged = lastChanged;
            instance.LastChangedBy = lastChangedBy;

            ToExternal(instance);
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