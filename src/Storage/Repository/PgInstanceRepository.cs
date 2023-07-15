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
        private static readonly string _readSqlFiltered = "select * from storage.readinstancefromquery (";
        private static readonly string _readSqlNoElements = "select * from storage.readinstancenoelements ($1)";

        private readonly ILogger<PgInstanceRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;

        static PgInstanceRepository()
        {
            for (int i = 1; i <= _paramTypes.Count(); i++)
            {
                _readSqlFiltered += $"${i}, ";
            }

            _readSqlFiltered = _readSqlFiltered[..^2] + ")";
        }

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
            try
            {
                return await GetInstancesInternal(queryParams, continuationToken, size);
            }
            catch (Exception e)
            {
                return new() { Count = 0, Instances = new(), Exception = e.Message };
            }
        }

        private async Task<InstanceQueryResponse> GetInstancesInternal(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            Instance instance = null;
            long id = -1;
            InstanceQueryResponse queryResponse = new() { Count = 0, Instances = new() };
            long continueIdx = string.IsNullOrEmpty(continuationToken) ? -1 : long.Parse(continuationToken);

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSqlFiltered);

            Dictionary<string, object> postgresParams = AddParametersFromQueryParams(queryParams);
            postgresParams.Add("_continue_idx", continueIdx);
            postgresParams.Add("_size", size);
            foreach (string name in _paramTypes.Keys)
            {
                pgcom.Parameters.AddWithValue(_paramTypes[name], postgresParams.ContainsKey(name) ? postgresParams[name] : DBNull.Value);
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
                        instance = reader.GetFieldValue<Instance>("instance");
                        queryResponse.Instances.Add(instance);
                        instance.Data = new();
                        previousId = id;
                    }

                    if (!reader.IsDBNull("element"))
                    {
                        instance.Data.Add(reader.GetFieldValue<DataElement>("element"));
                    }
                }

                SetStatuses(instance);
                queryResponse.ContinuationToken = queryResponse.Instances.Count == size ? id.ToString() : null;
            }

            queryResponse.Count = queryResponse.Instances.Count;
            return queryResponse;
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

        /// <summary>
        /// Add postgres parameters from query parameters
        /// </summary>
        /// <param name="queryParams">queryParams</param>
        /// <returns>Dictionary with postgres parameters</returns>
        private static Dictionary<string, object> AddParametersFromQueryParams(Dictionary<string, StringValues> queryParams)
        {
            Dictionary<string, object> postgresParams = new();
            foreach (KeyValuePair<string, StringValues> param in queryParams)
            {
                string queryParameter = param.Key;
                StringValues queryValues = param.Value;

                switch (queryParameter)
                {
                    case "instanceOwner.partyId":
                        if (queryValues.Count == 1)
                        {
                            postgresParams.Add($"{GetPgParamName(queryParameter)}", int.Parse(queryValues[0]));
                        }
                        else
                        {
                            postgresParams.Add($"{GetPgParamName(queryParameter)}s", queryValues.Select(p => int.Parse(p)).ToArray());
                        }

                        break;
                    case "size":
                    case "continuationToken":
                        // handled outside this method
                        break;
                    case "appId":
                        if (queryValues.Count == 1)
                        {
                            postgresParams.Add($"{GetPgParamName(queryParameter)}", queryValues[0]);
                        }
                        else
                        {
                            postgresParams.Add($"{GetPgParamName(queryParameter)}s", queryValues.ToArray());
                        }

                        break;
                    case "org":
                    case "excludeConfirmedBy":
                        postgresParams.Add(GetPgParamName(queryParameter), queryValues.ToArray());
                        break;
                    case "process.currentTask":
                        postgresParams.Add(GetPgParamName(queryParameter), queryValues[0]);
                        break;
                    case "archiveReference":
                        postgresParams.Add(GetPgParamName(queryParameter), queryValues[0].ToLower());
                        break;
                    case "status.isArchived":
                    case "status.isSoftDeleted":
                    case "status.isHardDeleted":
                    case "process.isComplete":
                    case "status.isArchivedOrSoftDeleted":
                    case "status.isActiveOrSoftDeleted":
                        postgresParams.Add(GetPgParamName(queryParameter), bool.Parse(queryValues[0]));
                        break;
                    case "sortBy":
                        postgresParams.Add(GetPgParamName(queryParameter), true);
                        break;
                    case "process.endEvent":
                    case "language":
                        break;
                    case "lastChanged":
                    case "created":
                        AddDateParam(queryParameter, queryValues, postgresParams, false);
                        break;
                    case "visibleAfter":
                    case "dueBefore":
                    case "process.ended":
                        AddDateParam(queryParameter, queryValues, postgresParams, true);
                        break;
                    default:
                        throw new ArgumentException($"Unknown query parameter: {queryParameter}");
                }
            }

            return postgresParams;
        }

        private static void AddDateParam(string dateParam, StringValues queryValues, Dictionary<string, object> postgresParams, bool valueAsString)
        {
            foreach (string value in queryValues)
            {
                string @operator = value.Split(':')[0];
                string dateValue = value.Substring(@operator.Length + 1);
                string postgresParamName = GetPgParamName($"{dateParam}_{@operator}");
                postgresParams.Add(postgresParamName, valueAsString ? dateValue : DateTimeHelper.ParseAndConvertToUniversalTime(dateValue));
            }
        }

        private static string GetPgParamName(string queryParameter)
        {
            return "_" + queryParameter.Replace(".", "_");
        }

        private static Dictionary<string, NpgsqlDbType> _paramTypes = new()
        {
            // This dictionary should be sorted alphabetically by key to match the sorted parameter list to the db function
            { "_appId", NpgsqlDbType.Text },
            { "_appIds", NpgsqlDbType.Text | NpgsqlDbType.Array },
            { "_archiveReference", NpgsqlDbType.Text },
            { "_continue_idx", NpgsqlDbType.Bigint },
            { "_created_eq", NpgsqlDbType.TimestampTz },
            { "_created_gt", NpgsqlDbType.TimestampTz },
            { "_created_gte", NpgsqlDbType.TimestampTz },
            { "_created_lt", NpgsqlDbType.TimestampTz },
            { "_created_lte", NpgsqlDbType.TimestampTz },
            { "_dueBefore_eq", NpgsqlDbType.Text },
            { "_dueBefore_gt", NpgsqlDbType.Text },
            { "_dueBefore_gte", NpgsqlDbType.Text },
            { "_dueBefore_lt", NpgsqlDbType.Text },
            { "_dueBefore_lte", NpgsqlDbType.Text },
            { "_excludeConfirmedBy", NpgsqlDbType.Text | NpgsqlDbType.Array },
            { "_instanceOwner_partyId", NpgsqlDbType.Integer },
            { "_instanceOwner_partyIds", NpgsqlDbType.Integer | NpgsqlDbType.Array },
            { "_lastChanged_eq", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gt", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gte", NpgsqlDbType.TimestampTz },
            { "_lastChanged_lt", NpgsqlDbType.TimestampTz },
            { "_lastChanged_lte", NpgsqlDbType.TimestampTz },
            { "_org", NpgsqlDbType.Text },
            { "_process_currentTask", NpgsqlDbType.Text },
            { "_process_ended_eq", NpgsqlDbType.Text },
            { "_process_ended_gt", NpgsqlDbType.Text },
            { "_process_ended_gte", NpgsqlDbType.Text },
            { "_process_ended_lt", NpgsqlDbType.Text },
            { "_process_ended_lte", NpgsqlDbType.Text },
            { "_process_isComplete", NpgsqlDbType.Boolean },
            { "_size", NpgsqlDbType.Integer },
            { "_sortBy", NpgsqlDbType.Boolean },
            { "_status_isActiveOrSoftDeleted", NpgsqlDbType.Boolean },
            { "_status_isArchived", NpgsqlDbType.Boolean },
            { "_status_isArchivedOrSoftDeleted", NpgsqlDbType.Boolean },
            { "_status_isHardDeleted", NpgsqlDbType.Boolean },
            { "_status_isSoftDeleted", NpgsqlDbType.Boolean },
            { "_visibleAfter_eq", NpgsqlDbType.Text },
            { "_visibleAfter_gt", NpgsqlDbType.Text },
            { "_visibleAfter_gte", NpgsqlDbType.Text },
            { "_visibleAfter_lt", NpgsqlDbType.Text },
            { "_visibleAfter_lte", NpgsqlDbType.Text },
        };
    }
}
