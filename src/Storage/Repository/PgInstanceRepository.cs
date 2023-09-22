using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
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
        private static readonly string _deleteSql = "select * from storage.deleteinstance ($1)";
        private static readonly string _insertSql = "call storage.insertinstance ($1, $2, $3, $4, $5, $6, $7, $8)";
        private static readonly string _upsertSql = "call storage.upsertinstance ($1, $2, $3, $4, $5, $6, $7, $8)";
        private static readonly string _readSql = "select * from storage.readinstance ($1)";
        private static readonly string _readSqlFiltered = "select * from storage.readinstancefromquery (";
        private static readonly string _readDeletedSql = "select * from storage.readdeletedinstances ()";
        private static readonly string _readDeletedElementsSql = "select * from storage.readdeletedelements ()";
        private static readonly string _readSqlNoElements = "select * from storage.readinstancenoelements ($1)";

        private readonly ILogger<PgInstanceRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;
        private readonly TelemetryClient _telemetryClient;

        static PgInstanceRepository()
        {
            for (int i = 1; i <= _paramTypes.Count; i++)
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
        /// <param name="telemetryClient">Telemetry client</param>
        public PgInstanceRepository(
            ILogger<PgInstanceRepository> logger,
            NpgsqlDataSource dataSource,
            TelemetryClient telemetryClient)
        {
            _logger = logger;
            _dataSource = dataSource;
            _telemetryClient = telemetryClient;
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
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(item.Id));

            int rc = (int)await pgcom.ExecuteScalarAsync();
            tracker.Track();
            return rc == 1;
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

        /// <inheritdoc/>
        public async Task<List<Instance>> GetHardDeletedInstances()
        {
            List<Instance> instances = new();

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readDeletedSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Instance i = reader.GetFieldValue<Instance>("instance");

                    // TODO move filter to db function
                    if (i.CompleteConfirmations != null && (i.CompleteConfirmations.Any(c => c.StakeholderId.ToLower().Equals(i.Org) && c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7))
                        || !i.Status.IsArchived))
                    {
                        instances.Add(i);
                    }
                }
            }

            tracker.Track();
            return instances;
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> GetHardDeletedDataElements()
        {
            List<DataElement> elements = new();
            try
            {
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readDeletedElementsSql);
                using TelemetryTracker tracker = new(_telemetryClient, pgcom);
                await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
                long previousId = -1;
                long id = -1;
                bool currentInstanceAllowsDelete = false;
                while (await reader.ReadAsync())
                {
                    id = reader.GetFieldValue<long>("id");
                    if (id != previousId)
                    {
                        Instance instance = reader.GetFieldValue<Instance>("instance");

                        // TODO move filter to db function
                        currentInstanceAllowsDelete =
                            instance.CompleteConfirmations != null &&
                            instance.CompleteConfirmations.Any(c => c.StakeholderId.ToLower().Equals(instance.Org) &&
                            c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7));
                        previousId = id;
                    }

                    if (currentInstanceAllowsDelete)
                    {
                        elements.Add(reader.GetFieldValue<DataElement>("element"));
                    }
                }

                tracker.Track();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting data elements {ex.Message}");
            }

            return elements;
        }

        private static string FormatManualFunctionCall(Dictionary<string, object> postgresParams)
        {
            string command = "select * from storage.readinstancefromquery (";
            foreach (string name in _paramTypes.Keys)
            {
                command += $"{name} => ";
                string value = "NULL";
                if (postgresParams.ContainsKey(name))
                {
                    value = _paramTypes[name] switch
                    {
                        NpgsqlDbType.Text => $"'{postgresParams[name]}'",
                        NpgsqlDbType.Bigint => $"{postgresParams[name]}",
                        NpgsqlDbType.TimestampTz => $"{((DateTime)postgresParams[name] != DateTime.MinValue ? "'" + ((DateTime)postgresParams[name]).ToString(DateTimeHelper.Iso8601UtcFormat, CultureInfo.InvariantCulture) + "'" : "NULL")}",
                        NpgsqlDbType.Integer => $"{postgresParams[name]}",
                        NpgsqlDbType.Boolean => $"{postgresParams[name]}",
                        NpgsqlDbType.Text | NpgsqlDbType.Array => ArrayVariableFromText((string[])postgresParams[name]),
                        NpgsqlDbType.Jsonb | NpgsqlDbType.Array => ArrayVariableFromJsonText((string[])postgresParams[name]),
                        NpgsqlDbType.Integer | NpgsqlDbType.Array => ArrayVariableFromInteger((int[])postgresParams[name]),
                        _ => throw new NotImplementedException()
                    };
                }

                command += value + ", ";
            }

            return command[..^2] + ");";
        }

        private static string ArrayVariableFromText(string[] arr)
        {
            string value = "'{";
            foreach (string param in arr)
            {
                value += $"\"{param}\", ";
            }

            return value[..^2] + "}'";
        }

        private static string ArrayVariableFromJsonText(string[] arr)
        {
            string value = "ARRAY [";
            foreach (string param in arr)
            {
                value += $"'{param}', ";
            }

            return value[..^2] + "]::jsonb[]";
        }

        private static string ArrayVariableFromInteger(int[] arr)
        {
            string value = "'{";
            foreach (int param in arr)
            {
                value += $"{param}, ";
            }

            return value[..^2] + "}'";
        }

        private async Task<InstanceQueryResponse> GetInstancesInternal(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            Instance instance = new(); // make sonarcloud happy
            long id = -1;
            DateTime lastChanged = DateTime.MinValue;
            InstanceQueryResponse queryResponse = new() { Count = 0, Instances = new() };
            long continueIdx = string.IsNullOrEmpty(continuationToken) ? -1 : long.Parse(continuationToken.Split(';')[1]);
            DateTime lastChangeIdx = string.IsNullOrEmpty(continuationToken) ? DateTime.MinValue : new DateTime(long.Parse(continuationToken.Split(';')[0]), DateTimeKind.Utc);

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSqlFiltered);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            Dictionary<string, object> postgresParams = AddParametersFromQueryParams(queryParams);
            postgresParams.Add("_continue_idx", continueIdx);
            postgresParams.Add("_lastChanged_idx", lastChangeIdx);
            postgresParams.Add("_size", size);
            foreach (string name in _paramTypes.Keys)
            {
                pgcom.Parameters.AddWithValue(_paramTypes[name], postgresParams.ContainsKey(name) ? postgresParams[name] : DBNull.Value);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(FormatManualFunctionCall(postgresParams));
            }

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                long previousId = -1;
                while (await reader.ReadAsync())
                {
                    id = reader.GetFieldValue<long>("id");
                    if (id != previousId)
                    {
                        if (previousId != -1)
                        {
                            SetStatuses(instance);
                        }

                        instance = reader.GetFieldValue<Instance>("instance");
                        lastChanged = instance.LastChanged ?? DateTime.MinValue;
                        queryResponse.Instances.Add(instance);
                        instance.Data = new();
                        previousId = id;
                    }

                    if (!reader.IsDBNull("element"))
                    {
                        instance.Data.Add(reader.GetFieldValue<DataElement>("element"));
                    }
                }

                if (id != -1)
                {
                    SetStatuses(instance);
                }

                queryResponse.ContinuationToken = queryResponse.Instances.Count == size ? $"{lastChanged.Ticks};{id}" : null;
            }

            queryResponse.Count = queryResponse.Instances.Count;
            tracker.Track();
            return queryResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid, bool includeElements = true)
        {
            Instance instance = null;
            long instanceInternalId = 0;

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(includeElements ? _readSql : _readSqlNoElements);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
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
                        instanceInternalId = reader.GetFieldValue<long>("id");
                        instance.Data = new();
                    }

                    if (includeElements && !reader.IsDBNull("element"))
                    {
                        instance.Data.Add(reader.GetFieldValue<DataElement>("element"));
                    }
                }

                if (instance == null)
                {
                    tracker.Track();
                    return (null, 0);
                }

                SetStatuses(instance);
            }

            tracker.Track();
            return (instance, instanceInternalId);
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
            else if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Count > 0 && !instance.Data.Exists(d => d.IsRead))
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
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, long.Parse(instance.InstanceOwner.PartyId));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instance.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instance);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, instance.Created ?? DateTime.Now);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, instance.LastChanged ?? DateTime.Now);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance.Org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance.AppId);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, instance?.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value);

            await pgcom.ExecuteNonQueryAsync();
            tracker.Track();

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
                    case "excludeConfirmedBy":
                        postgresParams.Add(GetPgParamName(queryParameter), GetExcludeConfirmedBy(queryValues));
                        break;
                    case "org":
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
                        postgresParams.Add("_sort_ascending", !queryValues[0].StartsWith("desc:", StringComparison.OrdinalIgnoreCase));
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

        private static string[] GetExcludeConfirmedBy(StringValues queryValues)
        {
            List<string> confirmations = new();

            foreach (var queryParameter in queryValues)
            {
                confirmations.Add($"[{{\"StakeholderId\":\"{queryParameter}\"}}]");
            }

            return confirmations.ToArray();
        }

        private static void AddDateParam(string dateParam, StringValues queryValues, Dictionary<string, object> postgresParams, bool valueAsString)
        {
            foreach (string value in queryValues)
            {
                string @operator = value.Split(':')[0];
                string dateValue = value[(@operator.Length + 1)..];
                string postgresParamName = GetPgParamName($"{dateParam}_{@operator}");
                postgresParams.Add(postgresParamName, valueAsString ? dateValue : DateTimeHelper.ParseAndConvertToUniversalTime(dateValue));
            }
        }

        private static string GetPgParamName(string queryParameter)
        {
            return "_" + queryParameter.Replace(".", "_");
        }

        private static readonly Dictionary<string, NpgsqlDbType> _paramTypes = new()
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
            { "_excludeConfirmedBy", NpgsqlDbType.Jsonb | NpgsqlDbType.Array },
            { "_instanceOwner_partyId", NpgsqlDbType.Integer },
            { "_instanceOwner_partyIds", NpgsqlDbType.Integer | NpgsqlDbType.Array },
            { "_lastChanged_eq", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gt", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gte", NpgsqlDbType.TimestampTz },
            { "_lastChanged_idx", NpgsqlDbType.TimestampTz },
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
            { "_sort_ascending", NpgsqlDbType.Boolean },
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
