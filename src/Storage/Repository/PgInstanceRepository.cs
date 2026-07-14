#nullable disable

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IInstanceRepository"/>.
/// </summary>
public class PgInstanceRepository : IInstanceRepository
{
    private const string ElementColumn = "element";
    private const string _readSqlFilteredInitial =
        "select * from storage.readinstancefromquery_v9 (";
    private readonly string _deleteSql = "select * from storage.deleteinstance ($1)";
    private readonly string _insertSql =
        "call storage.insertinstance_v3 (@_partyid, @_alternateid, @_instance, @_created, @_lastchanged,"
        + " @_org, @_appid, @_taskid, @_altinnmainversion, @_confirmed)";

    /// <summary>
    /// SQL for updating an instance.
    /// </summary>
    internal static readonly string UpdateSql =
        "select * from storage.updateinstance_v4 (@_alternateid, @_toplevelsimpleprops, @_datavalues,"
        + " @_completeconfirmations, @_presentationtexts, @_status, @_substatus, @_process, @_lastchanged, @_taskid, @_confirmed,"
        + " @_expectedinstanceversion, @_expectedprocessstateversion)";

    private readonly string _readSql = "select * from storage.readinstance_v2 ($1)";
    private readonly string _updateReadStatusSql =
        "select * from storage.updateinstance_readstatus ($1, $2)";
    private readonly string _readSqlFiltered = _readSqlFilteredInitial;
    private readonly string _readDeletedSql = "select * from storage.readdeletedinstances ()";
    private readonly string _readHardDeletedDataElementsForCleanupSql =
        "select * from storage.readharddeleteddataelementsforcleanup ()";
    private readonly string _readBlobVersionsForInstanceSql =
        "select * from storage.readblobversionsforinstance ($1)";
    private readonly string _readOrphanBlobVersionsForCleanupSql =
        "select * from storage.readorphanblobversionsforcleanup ()";
    private readonly string _readSqlNoElements =
        "select * from storage.readinstancenoelements_v2 ($1)";

    private readonly ILogger<PgInstanceRepository> _logger;
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgInstanceRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger to use when writing to logs.</param>
    /// <param name="dataSource">The npgsql data source.</param>
    public PgInstanceRepository(ILogger<PgInstanceRepository> logger, NpgsqlDataSource dataSource)
    {
        _logger = logger;
        _dataSource = dataSource;

        for (int i = 1; i <= _paramTypes.Count; i++)
        {
            _readSqlFiltered += $"${i}, ";
        }

        _readSqlFiltered = _readSqlFiltered[..^2] + ")";
    }

    /// <inheritdoc/>
    public async Task<InstanceInternal> Create(
        InstanceInternal instance,
        CancellationToken cancellationToken,
        int altinnMainVersion = 3
    )
    {
        // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
        instance.LastChanged =
            instance.LastChanged != null
                ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc)
                : null;

        instance.Id ??= Guid.NewGuid().ToString();
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
        pgcom.Parameters.AddWithValue(
            "_partyid",
            NpgsqlDbType.Bigint,
            long.Parse(instance.InstanceOwner.PartyId)
        );
        pgcom.Parameters.AddWithValue("_alternateid", NpgsqlDbType.Uuid, new Guid(instance.Id));
        pgcom.Parameters.AddWithValue("_instance", NpgsqlDbType.Jsonb, instance);
        pgcom.Parameters.AddWithValue(
            "_created",
            NpgsqlDbType.TimestampTz,
            instance.Created ?? DateTime.UtcNow
        );
        pgcom.Parameters.AddWithValue(
            "_lastchanged",
            NpgsqlDbType.TimestampTz,
            instance.LastChanged ?? DateTime.UtcNow
        );
        pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, instance.Org);
        pgcom.Parameters.AddWithValue("_appid", NpgsqlDbType.Text, instance.AppId);
        pgcom.Parameters.AddWithValue(
            "_taskid",
            NpgsqlDbType.Text,
            instance.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value
        );
        pgcom.Parameters.AddWithValue(
            "_altinnmainversion",
            NpgsqlDbType.Integer,
            altinnMainVersion
        );
        pgcom.Parameters.AddWithValue(
            "_confirmed",
            NpgsqlDbType.Boolean,
            instance.CompleteConfirmations != null
                && instance.CompleteConfirmations.Any(c => c.StakeholderId == instance.Org)
        );

        await pgcom.ExecuteNonQueryAsync(cancellationToken);

        instance.Data = [];
        instance.Versions = new StorageVersions(1, 1);
        instance.InternalId = 0;
        return instance;
    }

    /// <inheritdoc/>
    public async Task<bool> Delete(Guid instanceGuid, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        int rc = (int)await pgcom.ExecuteScalarAsync(cancellationToken);
        return rc == 1;
    }

    /// <inheritdoc/>
    public async Task<InstanceQueryResult> GetInstancesFromQuery(
        InstanceQueryParameters queryParams,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await GetInstancesInternal(queryParams, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error running GetInstancesFromQuery");
            return new() { Instances = [], Exception = e.Message };
        }
    }

    /// <inheritdoc/>
    public async Task<List<InstanceInternal>> GetHardDeletedInstances(
        CancellationToken cancellationToken
    )
    {
        List<InstanceInternal> instances = [];

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readDeletedSql);
        pgcom.CommandTimeout = 600; // 10 minutes
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                InstanceInternal i = await reader.GetFieldValueAsync<InstanceInternal>(
                    "instance",
                    cancellationToken
                );
                if (
                    (
                        i.CompleteConfirmations != null
                        && i.CompleteConfirmations.Exists(c =>
                            c.StakeholderId.ToLower().Equals(i.Org)
                            && c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7)
                        )
                    ) || !i.Status.IsArchived
                )
                {
                    instances.Add(i);
                }
            }
        }

        return instances;
    }

    /// <inheritdoc/>
    public async Task<List<DeletedDataElementInternal>> GetHardDeletedDataElements(
        CancellationToken cancellationToken
    )
    {
        Dictionary<
            string,
            (DataElementInternal DataElement, List<BlobVersionReferencesInternal> BlobVersions)
        > elements = [];
        List<string> elementOrder = [];
        try
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
                _readHardDeletedDataElementsForCleanupSql
            );
            pgcom.CommandTimeout = 600; // 10 minutes
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
            long previousId = -1;
            long id = -1;
            bool currentInstanceAllowsDelete = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                id = await reader.GetFieldValueAsync<long>("id", cancellationToken);
                if (id != previousId)
                {
                    InstanceInternal instance = await reader.GetFieldValueAsync<InstanceInternal>(
                        "instance",
                        cancellationToken
                    );
                    currentInstanceAllowsDelete =
                        instance.CompleteConfirmations != null
                        && instance.CompleteConfirmations.Exists(c =>
                            c.StakeholderId.Equals(instance.Org, StringComparison.OrdinalIgnoreCase)
                            && c.ConfirmedOn <= DateTime.UtcNow.AddDays(-7)
                        );
                    previousId = id;
                }

                if (currentInstanceAllowsDelete)
                {
                    DataElementInternal element =
                        await reader.GetFieldValueAsync<DataElementInternal>(
                            ElementColumn,
                            cancellationToken
                        );
                    string elementId = element.Id;
                    if (!elements.TryGetValue(elementId, out var elementWithVersions))
                    {
                        elementWithVersions = (element, []);
                        elements[elementId] = elementWithVersions;
                        elementOrder.Add(elementId);
                    }

                    Guid[] blobVersions = await reader.GetFieldValueAsync<Guid[]>(
                        "blobversions",
                        cancellationToken
                    );
                    if (blobVersions.Length > 0)
                    {
                        elementWithVersions.BlobVersions.Add(
                            await PgDataRepository.ReadBlobVersionReferencesAsync(
                                reader,
                                instanceGuidColumn: "blobversioninstanceguid",
                                appIdColumn: "blobversionappid",
                                blobStorageOrgColumn: "blobversionblobstorageorg",
                                storageAccountNumberColumn: "blobversionstorageaccountnumber",
                                cancellationToken: cancellationToken
                            )
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading hard-deleted data elements for cleanup");
        }

        return elementOrder
            .Select(elementId => new DeletedDataElementInternal(
                elements[elementId].DataElement,
                elements[elementId].BlobVersions
            ))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<BlobVersionReferencesInternal>> GetOrphanBlobVersionsForCleanup(
        CancellationToken cancellationToken
    )
    {
        List<BlobVersionReferencesInternal> orphanBlobVersions = [];
        try
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
                _readOrphanBlobVersionsForCleanupSql
            );
            pgcom.CommandTimeout = 600; // 10 minutes
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                orphanBlobVersions.Add(
                    await PgDataRepository.ReadBlobVersionReferencesAsync(
                        reader,
                        cancellationToken: cancellationToken
                    )
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading orphan blob versions for cleanup");
        }

        return orphanBlobVersions;
    }

    /// <inheritdoc/>
    public async Task<List<BlobVersionReferencesInternal>> GetBlobVersionsForInstance(
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        List<BlobVersionReferencesInternal> blobVersions = [];
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
            _readBlobVersionsForInstanceSql
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            blobVersions.Add(
                await PgDataRepository.ReadBlobVersionReferencesAsync(
                    reader,
                    cancellationToken: cancellationToken
                )
            );
        }

        return blobVersions;
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
                    NpgsqlDbType.TimestampTz =>
                        $"{((DateTime)value != DateTime.MinValue ? "'" + ((DateTime)value).ToString(DateTimeHelper.Iso8601UtcFormat, CultureInfo.InvariantCulture) + "'::timestamptz" : "NULL")}",
                    NpgsqlDbType.Integer => $"{value}",
                    NpgsqlDbType.Smallint => $"{value}::smallint",
                    NpgsqlDbType.Boolean => $"{value}",
                    NpgsqlDbType.Text | NpgsqlDbType.Array => ArrayVariableFromText(
                        (string[])value
                    ),
                    NpgsqlDbType.Jsonb | NpgsqlDbType.Array => ArrayVariableFromJsonText(
                        (string[])value
                    ),
                    NpgsqlDbType.Integer | NpgsqlDbType.Array => ArrayVariableFromInteger(
                        (int?[])value
                    ),
                    _ => throw new NotImplementedException(_paramTypes[name].ToString()),
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
        if (arr.Length == 0)
        {
            return "NULL";
        }

        StringBuilder value = new("'{");
        foreach (string param in arr)
        {
            value.Append($"\"{param}\", ");
        }

        return value.ToString()[..^2] + "}'";
    }

    private static string ArrayVariableFromJsonText(string[] arr)
    {
        if (arr.Length == 0)
        {
            return "NULL";
        }

        StringBuilder value = new("ARRAY [");
        foreach (string param in arr)
        {
            value.Append($"'{param}', ");
        }

        return value.ToString()[..^2] + "]::jsonb[]";
    }

    private static string ArrayVariableFromInteger(int?[] arr)
    {
        if (arr.Length == 0)
        {
            return "NULL";
        }

        StringBuilder value = new("'{");
        foreach (int? param in arr)
        {
            value.Append($"{param}, ");
        }

        return value.ToString()[..^2] + "}'";
    }

    private async Task<InstanceQueryResult> GetInstancesInternal(
        InstanceQueryParameters queryParams,
        CancellationToken cancellationToken
    )
    {
        DateTime lastChanged = DateTime.MinValue;
        InstanceQueryResult queryResult = new() { Instances = [] };

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSqlFiltered);

        Dictionary<string, object> postgresParams = queryParams.GeneratePostgreSQLParameters();
        postgresParams.Add("_includeElements", queryParams.IncludeDataElements);
        foreach (string name in _paramTypes.Keys)
        {
            pgcom.Parameters.AddWithValue(
                _paramTypes[name],
                postgresParams.TryGetValue(name, out object value) ? value : DBNull.Value
            );
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
#pragma warning disable CA2254 // Template should be a static expression
            _logger.LogDebug(FormatManualFunctionCall(postgresParams));
#pragma warning restore CA2254 // Template should be a static expression
        }

        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken))
        {
            long previousId = -1;
            long id = -1;
            InstanceInternal instance = new(); // make sonarcloud happy
            while (await reader.ReadAsync(cancellationToken))
            {
                id = await reader.GetFieldValueAsync<long>("id", cancellationToken);
                if (id != previousId)
                {
                    instance = await reader.GetFieldValueAsync<InstanceInternal>(
                        "instance",
                        cancellationToken
                    );
                    lastChanged = instance.LastChanged ?? DateTime.MinValue;
                    instance.InternalId = id;
                    instance.Versions = ReadVersionResult(reader);
                    instance.Data = [];
                    queryResult.Instances.Add(instance);
                    previousId = id;
                }

                if (!await reader.IsDBNullAsync(ElementColumn, cancellationToken))
                {
                    DataElementInternal element =
                        await reader.GetFieldValueAsync<DataElementInternal>(
                            ElementColumn,
                            cancellationToken
                        );
                    int versionOrdinal = reader.GetOrdinal("currentblobversion");
                    element.BlobVersionId = await reader.IsDBNullAsync(
                        versionOrdinal,
                        cancellationToken
                    )
                        ? null
                        : BlobVersionId.Encode(reader.GetGuid(versionOrdinal));
                    instance.Data.Add(element);
                }
            }

            queryResult.ContinuationToken =
                queryResult.Instances.Count == queryParams.Size
                    ? $"{lastChanged.Ticks};{id}"
                    : null;
        }

        Activity.Current?.AddTag("instanceCount", queryResult.Instances.Count.ToString());

        return queryResult;
    }

    /// <inheritdoc/>
    public async Task<InstanceInternal> GetOne(
        Guid instanceGuid,
        bool includeElements,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
            includeElements ? _readSql : _readSqlNoElements
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        return await ReadInstanceResultAsync(reader, includeElements, cancellationToken);
    }

    internal static async Task<InstanceInternal> ReadInstanceResultAsync(
        NpgsqlDataReader reader,
        bool includeElements,
        CancellationToken cancellationToken,
        Action<NpgsqlDataReader> firstRowCallback = null
    )
    {
        InstanceInternal instance = null;
        List<DataElementInternal> instanceData = [];
        StorageVersions versions = null;
        long instanceInternalId = 0;
        bool instanceCreated = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!instanceCreated)
            {
                instanceCreated = true;
                firstRowCallback?.Invoke(reader);
                instance = await reader.GetFieldValueAsync<InstanceInternal>(
                    "instance",
                    cancellationToken
                );
                versions = ReadVersionResult(reader);
                instanceInternalId = await reader.GetFieldValueAsync<long>("id", cancellationToken);
            }

            if (includeElements && !await reader.IsDBNullAsync(ElementColumn, cancellationToken))
            {
                DataElementInternal element = await reader.GetFieldValueAsync<DataElementInternal>(
                    ElementColumn,
                    cancellationToken
                );
                int versionOrdinal = reader.GetOrdinal("currentblobversion");
                string blobVersionId = await reader.IsDBNullAsync(versionOrdinal, cancellationToken)
                    ? null
                    : BlobVersionId.Encode(reader.GetGuid(versionOrdinal));
                element.BlobVersionId = blobVersionId;
                instanceData.Add(element);
            }
        }

        if (instance is null)
        {
            return null;
        }

        instance.Data = instanceData.OrderBy(x => x.Created).ToList();
        instance.Versions = versions;
        instance.InternalId = instanceInternalId;
        return instance;
    }

    /// <inheritdoc/>
    public async Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        CancellationToken cancellationToken,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        // Remove last decimal digit to make postgres TIMESTAMPTZ equal to json serialized DateTime
        instance.LastChanged =
            instance.LastChanged != null
                ? new DateTime((((DateTime)instance.LastChanged).Ticks / 10) * 10, DateTimeKind.Utc)
                : null;

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(UpdateSql);
        BuildUpdateCommand(
            instance,
            updateProperties,
            pgcom.Parameters,
            expectedInstanceVersion,
            expectedProcessStateVersion
        );

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw CreateMissingUpdateResultException("storage.updateinstance_v4");
        }

        InstanceInternal result = await ReadUpdatedInstanceAsync(
            reader,
            instance.InternalId,
            cancellationToken
        );
        result.Data = instance.Data;
        return result;
    }

    /// <inheritdoc/>
    public async Task<InstanceInternal> UpdateReadStatus(
        InstanceInternal instanceInternal,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateReadStatusSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceInternal.Id));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instanceInternal.Status);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw CreateMissingUpdateResultException("storage.updateinstance_readstatus");
        }

        InstanceInternal result = await ReadUpdatedInstanceAsync(
            reader,
            instanceInternal.InternalId,
            cancellationToken
        );
        result.Data = instanceInternal.Data;
        return result;
    }

    /// <summary>
    /// Arguments passed to storage.updateinstance_v4.
    /// </summary>
    internal sealed record InstanceUpdateCommandArguments(
        Guid AlternateId,
        string TopLevelSimpleProperties,
        object DataValues,
        object CompleteConfirmations,
        object PresentationTexts,
        object Status,
        object Substatus,
        object Process,
        DateTime LastChanged,
        object TaskId,
        object Confirmed,
        object ExpectedInstanceVersion,
        object ExpectedProcessStateVersion
    );

    /// <summary>
    /// Builds the update command for the instance.
    /// </summary>
    /// <param name="instance">Instance</param>
    /// <param name="updateProperties">Updated props</param>
    /// <param name="parameters">Parameters</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    internal static void BuildUpdateCommand(
        InstanceInternal instance,
        List<string> updateProperties,
        NpgsqlParameterCollection parameters,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        InstanceUpdateCommandArguments arguments = BuildUpdateCommandArguments(
            instance,
            updateProperties,
            expectedInstanceVersion,
            expectedProcessStateVersion
        );
        AddUpdateCommandParameters(arguments, parameters);
    }

    internal static InstanceUpdateCommandArguments BuildUpdateCommandArguments(
        InstanceInternal instance,
        List<string> updateProperties,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    ) =>
        new(
            new Guid(instance.Id),
            CustomSerializer.Serialize(instance, updateProperties),
            updateProperties.Contains(nameof(instance.DataValues))
                ? instance.DataValues
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.CompleteConfirmations))
                ? instance.CompleteConfirmations
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.PresentationTexts))
                ? instance.PresentationTexts
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Status))
                ? CustomSerializer.Serialize(instance.Status, updateProperties)
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Status.Substatus))
                ? instance.Status.Substatus
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Process)) ? instance.Process : DBNull.Value,
            instance.LastChanged ?? DateTime.UtcNow,
            instance.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value,
            instance.CompleteConfirmations != null
            && instance.CompleteConfirmations.Any(c => c.StakeholderId == instance.Org)
                ? true
                : DBNull.Value,
            expectedInstanceVersion ?? (object)DBNull.Value,
            expectedProcessStateVersion ?? (object)DBNull.Value
        );

    internal static void AddUpdateCommandParameters(
        InstanceUpdateCommandArguments arguments,
        NpgsqlParameterCollection parameters
    )
    {
        parameters.AddWithValue("_alternateid", NpgsqlDbType.Uuid, arguments.AlternateId);
        parameters.AddWithValue(
            "_toplevelsimpleprops",
            NpgsqlDbType.Jsonb,
            arguments.TopLevelSimpleProperties
        );
        parameters.AddWithValue("_datavalues", NpgsqlDbType.Jsonb, arguments.DataValues);
        parameters.AddWithValue(
            "_completeconfirmations",
            NpgsqlDbType.Jsonb,
            arguments.CompleteConfirmations
        );
        parameters.AddWithValue(
            "_presentationtexts",
            NpgsqlDbType.Jsonb,
            arguments.PresentationTexts
        );
        parameters.AddWithValue("_status", NpgsqlDbType.Jsonb, arguments.Status);
        parameters.AddWithValue("_substatus", NpgsqlDbType.Jsonb, arguments.Substatus);
        parameters.AddWithValue("_process", NpgsqlDbType.Jsonb, arguments.Process);
        parameters.AddWithValue("_lastchanged", NpgsqlDbType.TimestampTz, arguments.LastChanged);
        parameters.AddWithValue("_taskid", NpgsqlDbType.Text, arguments.TaskId);
        parameters.AddWithValue("_confirmed", NpgsqlDbType.Boolean, arguments.Confirmed);
        parameters.AddWithValue(
            "_expectedinstanceversion",
            NpgsqlDbType.Integer,
            arguments.ExpectedInstanceVersion
        );
        parameters.AddWithValue(
            "_expectedprocessstateversion",
            NpgsqlDbType.Integer,
            arguments.ExpectedProcessStateVersion
        );
    }

    internal static async Task<InstanceInternal> ReadUpdatedInstanceAsync(
        NpgsqlDataReader reader,
        long instanceInternalId,
        CancellationToken cancellationToken
    )
    {
        string result = await reader.GetFieldValueAsync<string>("result", cancellationToken);
        if (result != "ok")
        {
            throw result switch
            {
                "not_found" => CreateInstanceNotFoundException(),
                "instance_version_mismatch" => CreateInstanceVersionMismatchException(reader),
                "process_state_version_mismatch" => CreateProcessStateVersionMismatchException(
                    reader
                ),
                _ => new UnreachableException($"Unexpected instance update result '{result}'."),
            };
        }

        InstanceInternal instance = await reader.GetFieldValueAsync<InstanceInternal>(
            "updatedInstance",
            cancellationToken
        );
        StorageVersions versions = ReadVersionResult(reader);
        instance.Versions = versions;
        instance.InternalId = instanceInternalId;
        return instance;
    }

    internal static StorageVersions ReadVersionResult(NpgsqlDataReader reader) =>
        new(
            reader.GetInt32(reader.GetOrdinal("instanceversion")),
            reader.GetInt32(reader.GetOrdinal("processstateversion"))
        );

    internal static RepositoryException CreateInstanceNotFoundException(string instanceId = null) =>
        new(
            instanceId is null
                ? "Instance was not found."
                : $"Instance {instanceId} was not found.",
            System.Net.HttpStatusCode.NotFound
        );

    internal static UnreachableException CreateMissingUpdateResultException(string functionName) =>
        new(
            $"{functionName} returned no result row. The SQL function must return a row with a result code."
        );

    private static InstanceVersionMismatchException CreateInstanceVersionMismatchException(
        NpgsqlDataReader reader
    )
    {
        StorageVersions versions = ReadVersionResult(reader);
        return new InstanceVersionMismatchException(
            versions.InstanceVersion,
            versions.ProcessStateVersion
        );
    }

    private static ProcessStateVersionMismatchException CreateProcessStateVersionMismatchException(
        NpgsqlDataReader reader
    )
    {
        StorageVersions versions = ReadVersionResult(reader);
        return new ProcessStateVersionMismatchException(
            versions.InstanceVersion,
            versions.ProcessStateVersion
        );
    }

    private static readonly Dictionary<string, NpgsqlDbType> _paramTypes = new()
    {
        // This dictionary should be sorted alphabetically by key to match the sorted parameter list to the db function
        { "_A3Ref", NpgsqlDbType.Text },
        { "_appId", NpgsqlDbType.Text },
        { "_appIds", NpgsqlDbType.Text | NpgsqlDbType.Array },
        { "_archiveReference", NpgsqlDbType.Text },
        { "_confirmed", NpgsqlDbType.Boolean },
        { "_continue_idx", NpgsqlDbType.Bigint },
        { "_created_eq", NpgsqlDbType.TimestampTz },
        { "_created_gt", NpgsqlDbType.TimestampTz },
        { "_created_gte", NpgsqlDbType.TimestampTz },
        { "_created_lt", NpgsqlDbType.TimestampTz },
        { "_created_lte", NpgsqlDbType.TimestampTz },
        { "_dataValues_A2ArchRef", NpgsqlDbType.Text },
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
