using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;

using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IInstanceRepository"/>.
    /// </summary>
    public class PgInstanceRepository : IInstanceRepository
    {
        private const string _readSqlFilteredInitial = "select * from storage.readinstancefromquery_v5 (";
        private readonly string _deleteSql = "select * from storage.deleteinstance ($1)";
        private readonly string _insertSql = "call storage.insertinstance_v2 (@_partyid, @_alternateid, @_instance, @_created, @_lastchanged, @_org, @_appid, @_taskid, @_altinnmainversion)";
        private readonly string _updateSql = "select * from storage.updateinstance_v2 (@_alternateid, @_toplevelsimpleprops, @_datavalues, @_completeconfirmations, @_presentationtexts, @_status, @_substatus, @_process, @_lastchanged, @_taskid)";
        private readonly string _insertInstanceEventsSqlTemplate = "INSERT INTO storage.instanceevents(instance, alternateid, event) VALUES ";

        private readonly string _readSql = "select * from storage.readinstance ($1)";
        private readonly string _readSqlFiltered = _readSqlFilteredInitial;
        private readonly string _readDeletedSql = "select * from storage.readdeletedinstances ()";
        private readonly string _readDeletedElementsSql = "select * from storage.readdeletedelements ()";
        private readonly string _readSqlNoElements = "select * from storage.readinstancenoelements ($1)";

        private readonly ILogger<PgInstanceRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;
        private readonly TelemetryClient _telemetryClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgInstanceRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="telemetryClient">Telemetry client</param>
        public PgInstanceRepository(
            ILogger<PgInstanceRepository> logger,
            NpgsqlDataSource dataSource,
            TelemetryClient telemetryClient = null)
        {
            _logger = logger;
            _dataSource = dataSource;
            _telemetryClient = telemetryClient;

            for (int i = 1; i <= _paramTypes.Count; i++)
            {
                _readSqlFiltered += $"${i}, ";
            }

            _readSqlFiltered = _readSqlFiltered[..^2] + ")";
        }

        /// <inheritdoc/>
        public async Task<Instance> Create(Instance instance, int altinnMainVersion = 3)
        {
            // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
            instance.LastChanged = instance.LastChanged != null ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc) : null;

            instance.Id ??= Guid.NewGuid().ToString();
            ToInternal(instance);
            instance.Data = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue("_partyid", NpgsqlDbType.Bigint, long.Parse(instance.InstanceOwner.PartyId));
            pgcom.Parameters.AddWithValue("_alternateid", NpgsqlDbType.Uuid, new Guid(instance.Id));
            pgcom.Parameters.AddWithValue("_instance", NpgsqlDbType.Jsonb, instance);
            pgcom.Parameters.AddWithValue("_created", NpgsqlDbType.TimestampTz, instance.Created ?? DateTime.UtcNow);
            pgcom.Parameters.AddWithValue("_lastchanged", NpgsqlDbType.TimestampTz, instance.LastChanged ?? DateTime.UtcNow);
            pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, instance.Org);
            pgcom.Parameters.AddWithValue("_appid", NpgsqlDbType.Text, instance.AppId);
            pgcom.Parameters.AddWithValue("_taskid", NpgsqlDbType.Text, instance.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value);
            pgcom.Parameters.AddWithValue("_altinnmainversion", NpgsqlDbType.Integer, altinnMainVersion);

            await pgcom.ExecuteNonQueryAsync();
            tracker.Track();

            instance.Data = [];
            return ToExternal(instance);
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(Instance instance)
        {
            ToInternal(instance);
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instance.Id));

            int rc = (int)await pgcom.ExecuteScalarAsync();
            tracker.Track();
            return rc == 1;
        }

        /// <inheritdoc/>
        public async Task<InstanceQueryResponse> GetInstancesFromQuery(InstanceQueryParameters queryParams, bool includeDataElements)
        {
            try
            {
                return await GetInstancesInternal(queryParams, includeDataElements);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running GetInstancesFromQuery");
                return new() { Count = 0, Instances = [], Exception = e.Message };
            }
        }

        /// <inheritdoc/>
        public async Task<List<Instance>> GetHardDeletedInstances()
        {
            List<Instance> instances = [];

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readDeletedSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Instance i = await reader.GetFieldValueAsync<Instance>("instance");
                    if ((i.CompleteConfirmations != null && i.CompleteConfirmations.Exists(c => c.StakeholderId.ToLower().Equals(i.Org) && c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7)))
                    || !i.Status.IsArchived)
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
            List<DataElement> elements = [];
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
                    id = await reader.GetFieldValueAsync<long>("id");
                    if (id != previousId)
                    {
                        Instance instance = await reader.GetFieldValueAsync<Instance>("instance");
                        currentInstanceAllowsDelete =
                            instance.CompleteConfirmations != null &&
                            instance.CompleteConfirmations.Exists(c => c.StakeholderId.Equals(instance.Org, StringComparison.OrdinalIgnoreCase) &&
                            c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7));
                        previousId = id;
                    }

                    if (currentInstanceAllowsDelete)
                    {
                        elements.Add(await reader.GetFieldValueAsync<DataElement>("element"));
                    }
                }

                tracker.Track();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting data elements");
            }

            return elements;
        }

        private static string FormatManualFunctionCall(Dictionary<string, object> postgresParams)
        {
            StringBuilder command = new(_readSqlFilteredInitial);
            foreach (string name in _paramTypes.Keys)
            {
                command.Append($"{name} => ");
                if (postgresParams.TryGetValue(name, out object value))
                {
                    value = _paramTypes[name] switch
                    {
                        NpgsqlDbType.Text => $"'{value}'",
                        NpgsqlDbType.Bigint => $"{value}",
                        NpgsqlDbType.TimestampTz => $"{((DateTime)value != DateTime.MinValue ? "'" + ((DateTime)value).ToString(DateTimeHelper.Iso8601UtcFormat, CultureInfo.InvariantCulture) + "'::timestamptz" : "NULL")}",
                        NpgsqlDbType.Integer => $"{value}",
                        NpgsqlDbType.Smallint => $"{value}",
                        NpgsqlDbType.Boolean => $"{value}",
                        NpgsqlDbType.Text | NpgsqlDbType.Array => ArrayVariableFromText((string[])value),
                        NpgsqlDbType.Jsonb | NpgsqlDbType.Array => ArrayVariableFromJsonText((string[])value),
                        NpgsqlDbType.Integer | NpgsqlDbType.Array => ArrayVariableFromInteger((int?[])value),
                        _ => throw new NotImplementedException(_paramTypes[name].ToString())
                    };
                }
                else
                {
                    value = "NULL";
                }

                command.Append(value + ", ");
            }

            return command.ToString()[..^2] + ");";
        }

        private static string ArrayVariableFromText(string[] arr)
        {
            StringBuilder value = new("'{");
            foreach (string param in arr)
            {
                value.Append($"\"{param}\", ");
            }

            return value.ToString()[..^2] + "}'";
        }

        private static string ArrayVariableFromJsonText(string[] arr)
        {
            StringBuilder value = new("ARRAY [");
            foreach (string param in arr)
            {
                value.Append($"'{param}', ");
            }

            return value.ToString()[..^2] + "]::jsonb[]";
        }

        private static string ArrayVariableFromInteger(int?[] arr)
        {
            StringBuilder value = new("'{");
            foreach (int? param in arr)
            {
                value.Append($"{param}, ");
            }

            return value.ToString()[..^2] + "}'";
        }

        private async Task<InstanceQueryResponse> GetInstancesInternal(
            InstanceQueryParameters queryParams,
            bool includeDataelements)
        {
            DateTime lastChanged = DateTime.MinValue;
            InstanceQueryResponse queryResponse = new() { Count = 0, Instances = [] };

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSqlFiltered);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            Dictionary<string, object> postgresParams = queryParams.GeneratePostgreSQLParameters();
            postgresParams.Add("_includeElements", includeDataelements);
            foreach (string name in _paramTypes.Keys)
            {
                pgcom.Parameters.AddWithValue(_paramTypes[name], postgresParams.TryGetValue(name, out object value) ? value : DBNull.Value);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogDebug(FormatManualFunctionCall(postgresParams));
#pragma warning restore CA2254 // Template should be a static expression
            }

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                long previousId = -1;
                long id = -1;
                Instance instance = new(); // make sonarcloud happy
                while (await reader.ReadAsync())
                {
                    id = await reader.GetFieldValueAsync<long>("id");
                    if (id != previousId)
                    {
                        if (previousId != -1)
                        {
                            ToExternal(instance);
                        }

                        instance = await reader.GetFieldValueAsync<Instance>("instance");
                        lastChanged = instance.LastChanged ?? DateTime.MinValue;
                        queryResponse.Instances.Add(instance);
                        instance.Data = [];
                        previousId = id;
                    }

                    if (!await reader.IsDBNullAsync("element"))
                    {
                        instance.Data.Add(await reader.GetFieldValueAsync<DataElement>("element"));
                    }
                }

                if (id != -1)
                {
                    ToExternal(instance);
                }

                queryResponse.ContinuationToken = queryResponse.Instances.Count == queryParams.Size ? $"{lastChanged.Ticks};{id}" : null;
            }

            queryResponse.Count = queryResponse.Instances.Count;
            tracker.Add("Count", queryResponse.Count.ToString());
            if (tracker.ElapsedMilliseconds > 500)
            {
                tracker.Add("Call", FormatManualFunctionCall(postgresParams));
            }

            tracker.Track();
            return queryResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(Guid instanceGuid, bool includeElements)
        {
            Instance instance = null;
            List<DataElement> instanceData = [];
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
                        instance = await reader.GetFieldValueAsync<Instance>("instance");
                        instanceInternalId = await reader.GetFieldValueAsync<long>("id");
                    }

                    if (includeElements && !await reader.IsDBNullAsync("element"))
                    {
                        instanceData.Add(await reader.GetFieldValueAsync<DataElement>("element"));
                    }
                }

                if (instance is null)
                {
                    tracker.Track();
                    return (null, 0);
                }
            }

            // Present instance data elements in chronological order
            instance.Data = instanceData.OrderBy(x => x.Created).ToList();
            ToExternal(instance);

            tracker.Track();
            return (instance, instanceInternalId);
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance instance, List<string> updateProperties)
        {
            // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
            instance.LastChanged = instance.LastChanged != null ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc) : null;
            List<DataElement> dataElements = instance.Data;

            ToInternal(instance);
            instance.Data = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            BuildUpdateCommand(instance, updateProperties, pgcom.Parameters);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                instance = await reader.GetFieldValueAsync<Instance>("updatedInstance");
            }

            instance.Data = dataElements;
            tracker.Track();
            return ToExternal(instance);
        }

        private void BuildUpdateCommand(Instance instance, List<string> updateProperties, NpgsqlParameterCollection parameters)
        {
            parameters.AddWithValue("_alternateid", NpgsqlDbType.Uuid, new Guid(instance.Id));
            parameters.AddWithValue("_toplevelsimpleprops", NpgsqlDbType.Jsonb, CustomSerializer.Serialize(instance, updateProperties));
            parameters.AddWithValue("_datavalues", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.DataValues)) ? instance.DataValues : DBNull.Value);
            parameters.AddWithValue("_completeconfirmations", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.CompleteConfirmations)) ? instance.CompleteConfirmations : DBNull.Value);
            parameters.AddWithValue("_presentationtexts", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.PresentationTexts)) ? instance.PresentationTexts : DBNull.Value);
            parameters.AddWithValue("_status", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.Status)) ? CustomSerializer.Serialize(instance.Status, updateProperties) : DBNull.Value);
            parameters.AddWithValue("_substatus", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.Status.Substatus)) ? instance.Status.Substatus : DBNull.Value);
            parameters.AddWithValue("_process", NpgsqlDbType.Jsonb, updateProperties.Contains(nameof(instance.Process)) ? instance.Process : DBNull.Value);
            parameters.AddWithValue("_lastchanged", NpgsqlDbType.TimestampTz, instance.LastChanged ?? DateTime.UtcNow);
            parameters.AddWithValue("_taskid", NpgsqlDbType.Text, instance.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value);
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance instance, List<string> updateProperties, List<InstanceEvent> events)
        {
            if (events.Count > 500) 
            {
                // Batching is implemented by simply appending multiple value clauses to the insert statement
                // There is a limit to how many parameters PostgreSQL can accept (the count must fit within a 16 bit integer)
                // so we limit the event count to some large number we never expect to hit.
                throw new InvalidOperationException("Too many events to insert in one go");
            }
            else if (events.Count == 0)
            {
                return await Update(instance, updateProperties);
            }

            // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
            instance.LastChanged = instance.LastChanged != null ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc) : null;
            List<DataElement> dataElements = instance.Data;

            ToInternal(instance);
            instance.Data = null;
            await using var batch = _dataSource.CreateBatch();

            var updateCommand = new NpgsqlBatchCommand(_updateSql);
            BuildUpdateCommand(instance, updateProperties, updateCommand.Parameters);
            batch.BatchCommands.Add(updateCommand);

            var insertEventsSql = new StringBuilder(_insertInstanceEventsSqlTemplate);

            for (int i = 0; i < events.Count; i++)
            {
                insertEventsSql.Append($"{(i == 0 ? string.Empty : ", ")}(@_{i}instance, @_{i}alternateid, jsonb_strip_nulls(@_{i}event))");
            }

            var insertEventsComand = new NpgsqlBatchCommand(insertEventsSql.ToString());
            for (int i = 0; i < events.Count; i++)
            {
                var instanceEvent = events[i];
                instanceEvent.Id ??= Guid.NewGuid();
                insertEventsComand.Parameters.AddWithValue($"_{i}instance", NpgsqlDbType.Uuid, new Guid(instanceEvent.InstanceId.Split('/').Last()));
                insertEventsComand.Parameters.AddWithValue($"_{i}alternateid", NpgsqlDbType.Uuid, instanceEvent.Id);
                insertEventsComand.Parameters.AddWithValue($"_{i}event", NpgsqlDbType.Jsonb, instanceEvent);
            }

            batch.BatchCommands.Add(insertEventsComand);
            
            await using var reader = await batch.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                instance = await reader.GetFieldValueAsync<Instance>("updatedInstance");
            }

            instance.Data = dataElements; // TODO: requery instead?
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
            { "_includeElements", NpgsqlDbType.Boolean },
            { "_instanceOwner_partyId", NpgsqlDbType.Integer },
            { "_instanceOwner_partyIds", NpgsqlDbType.Integer | NpgsqlDbType.Array },
            { "_lastChanged_eq", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gt", NpgsqlDbType.TimestampTz },
            { "_lastChanged_gte", NpgsqlDbType.TimestampTz },
            { "_lastChanged_idx", NpgsqlDbType.TimestampTz },
            { "_lastChanged_lt", NpgsqlDbType.TimestampTz },
            { "_lastChanged_lte", NpgsqlDbType.TimestampTz },
            { "_mainVersionInclude", NpgsqlDbType.Smallint },
            { "_mainVersionExclude", NpgsqlDbType.Smallint },
            { "_msgBoxInterval_eq", NpgsqlDbType.TimestampTz },
            { "_msgBoxInterval_gt", NpgsqlDbType.TimestampTz },
            { "_msgBoxInterval_gte", NpgsqlDbType.TimestampTz },
            { "_msgBoxInterval_lt", NpgsqlDbType.TimestampTz },
            { "_msgBoxInterval_lte", NpgsqlDbType.TimestampTz },
            { "_org", NpgsqlDbType.Text },
            { "_process_currentTask", NpgsqlDbType.Text },
            { "_process_ended_eq", NpgsqlDbType.Text },
            { "_process_ended_gt", NpgsqlDbType.Text },
            { "_process_ended_gte", NpgsqlDbType.Text },
            { "_process_ended_lt", NpgsqlDbType.Text },
            { "_process_ended_lte", NpgsqlDbType.Text },
            { "_process_isComplete", NpgsqlDbType.Boolean },
            { "_search_string", NpgsqlDbType.Text },
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
