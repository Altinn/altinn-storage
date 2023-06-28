using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
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
    public class PgInstanceRepository: IInstanceRepository
    {
        private static readonly string _deleteSql = "call storage.deleteinstance ($1)";
        private static readonly string _insertSql = "call storage.insertinstance ($1, $2, $3, $4, $5, $6, $7, $8)";
        private static readonly string _upsertSql = "call storage.upsertinstance ($1, $2, $3, $4, $5, $6, $7, $8)";
        private static readonly string _readSql = "select * from storage.readinstance ($1)";
        private static readonly string _readSqlNoElements = "select * from storage.readinstancenoelements ($1)";

        private readonly ILogger<PgInstanceRepository> _logger;
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
        public async Task<Instance> Create(Instance item)
        {
            Instance updatedInstance = await Upsert(item, true);
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
                    if (queryBuilder.Any())
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

        private async Task<(IQueryable<Instance> InstancesAsQueriable, long Count)> GetInstances(int size, long continueIdx, Dictionary<string, StringValues> queryParams)
        {
            Instance instance = null;
            List<Instance> instances = new();
            long id = -1;
            string partyPredicate = null;
            int partyId = 0;
            int[] partyIdArray = null;

            if (queryParams.TryGetValue("instanceOwner.partyId", out StringValues partyIdStringValues))
            {
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
            }

            string taskPredicate = queryParams.ContainsKey("process.currentTask") ? " AND taskId ILIKE $4" : null;
            string appIdPredicate = queryParams.ContainsKey("appId") ? " AND appId ILIKE $6" : null;
            string orgPredicate = appIdPredicate == null && queryParams.ContainsKey("org") ? " AND org ILIKE $5" : null;
            (string lastChangedPredicate, DateTime lastChanged) = InstanceQueryHelper.ConvertTimestampParameter("lastChanged", queryParams, 7);
            (string createdPredicate, DateTime created) = InstanceQueryHelper.ConvertTimestampParameter("created", queryParams, 8);

            string readManySql =
                $"WITH instances AS " +
                $"( " +
                $"    SELECT id, instance FROM storage.instances " +
                $"    WHERE id > $1 " + partyPredicate + taskPredicate + orgPredicate + appIdPredicate + lastChangedPredicate + createdPredicate +
                $"    ORDER BY id " +
                $"    FETCH FIRST $2 ROWS ONLY " +
                $") " +
                $"    SELECT instances.id, instances.instance, element FROM instances LEFT JOIN storage.dataelements d ON instances.id = d.instanceInternalId " +
                $"    ORDER BY instances.id ";

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(readManySql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, continueIdx);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, size);
            if (partyIdStringValues != default(StringValues))
            {
                if (partyIdStringValues.Count == 1)
                {
                    pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, partyId);
                }
                else if (partyIdStringValues.Count > 1)
                {
                    pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Integer, partyIdArray);
                }
            }
            else
            {
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, 0);
            }

            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, taskPredicate != null ? queryParams["process.currentTask"].First() : DBNull.Value);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, orgPredicate != null ? queryParams["org"].First() : DBNull.Value);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, appIdPredicate != null ? queryParams["appId"].First() : DBNull.Value);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, lastChanged);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, created);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                long previousId = -1;
                while (await reader.ReadAsync())
                {
                    id = reader.GetFieldValue<long>("id");
                    if (id != previousId)
                    {
                        SetStatuses(instance);
                        instance = reader.GetFieldValue<Instance>("instance");
                        instances.Add(instance);
                        instance.Data = new();
                        previousId = id;
                    }

                    if (!reader.IsDBNull("element"))
                    {
                        instance.Data.Add(reader.GetFieldValue<DataElement>("element"));
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
                        instance = reader.GetFieldValue<Instance>("instance");
                        internalId = reader.GetFieldValue<long>("id");
                        instance.Data = new();
                    }

                    if (includeElements && !reader.IsDBNull("element"))
                    {
                        instance.Data.Add(reader.GetFieldValue<DataElement>("element"));
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

        private static void SetStatuses(Instance instance)
        {
            if (instance == null)
            {
                return;
            }

            if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Exists(d => !d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.UpdatedSinceLastReview;
            }
            else if (instance.Status.ReadStatus == ReadStatus.Read && !instance.Data.Exists(d => d.IsRead))
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
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instance);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, instance.Created ?? DateTime.Now);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, instance.LastChanged ?? DateTime.Now);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance.Org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance.AppId);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance?.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value);

            await pgcom.ExecuteNonQueryAsync();

            return ToExternal(instance);
        }

        private static void ToInternal(Instance instance)
        {
            if (instance.Id.Contains('/', StringComparison.Ordinal))
            {
                instance.Id = instance.Id.Split('/')[1];
            }
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
