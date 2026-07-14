#nullable disable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// PostgreSQL implementation of aggregate instance mutations.
/// </summary>
public sealed class PgInstanceMutationRepository(
    ILogger<PgInstanceMutationRepository> logger,
    NpgsqlDataSource dataSource,
    OutboxInsertRowFactory outboxInsertRowFactory
) : IInstanceMutationRepository
{
    internal const string ApplyMutationSql =
        "select * from storage.applyinstancemutation($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)";
    private static readonly JsonSerializerOptions OmitNullPropertiesJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string TryReplayAdmissionSql =
        "select storage.tryreplayinstancemutation_v2($1, $2, $3, $4, $5) as createddataelementids";
    private const string ReadInstanceSql = "select * from storage.readinstance_v2($1)";
    private readonly ILogger<PgInstanceMutationRepository> _logger = logger;
    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly OutboxInsertRowFactory _outboxInsertRowFactory = outboxInsertRowFactory;

    /// <inheritdoc/>
    public async Task<InstanceMutationApplyResult> TryReplayAdmission(
        Guid instanceGuid,
        int expectedInstanceVersion,
        int currentInstanceVersion,
        int currentProcessStateVersion,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<string> createdDataElementIds;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(
            cancellationToken
        );
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken
        );

        await using (NpgsqlCommand cmd = new(TryReplayAdmissionSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, idempotencyKey);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, expectedInstanceVersion);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, currentInstanceVersion);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, currentProcessStateVersion);

            try
            {
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(
                    cancellationToken
                );
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new UnreachableException("Replay admission function returned no result.");
                }

                createdDataElementIds = ReadTextArray(reader, "createddataelementids");
            }
            catch (PostgresException exception) when (exception.SqlState == "AM001")
            {
                throw CreateApplyMutationException(instanceGuid, exception);
            }
        }

        InstanceInternal instance = await ReadInstanceForReplay(
            connection,
            transaction,
            instanceGuid,
            cancellationToken
        );
        if (instance is null)
        {
            throw new UnreachableException(
                "Replay admission succeeded but follow-up instance read returned no result."
            );
        }

        if (instance.Status?.IsHardDeleted == true)
        {
            throw CreateInstanceHardDeletedException(instanceGuid);
        }

        EnsureReplaySnapshotMatchesAdmission(
            instance,
            currentInstanceVersion,
            currentProcessStateVersion
        );

        await transaction.CommitAsync(cancellationToken);

        return new InstanceMutationApplyResult(true, createdDataElementIds, instance);
    }

    /// <inheritdoc/>
    public async Task<InstanceMutationApplyResult> Apply(
        Guid instanceGuid,
        long instanceInternalId,
        InstanceMutationCommit mutation,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand cmd = _dataSource.CreateCommand(ApplyMutationSql);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            mutation.ExpectedInstanceVersion
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            mutation.ExpectedProcessStateVersion
        );
        AddNullableParameter(cmd.Parameters, NpgsqlDbType.Uuid, mutation.IdempotencyKey);
        cmd.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            NormalizePayloadTimestamp(mutation.LastChanged ?? DateTime.UtcNow)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Text,
            mutation.LastChangedBy ?? mutation.InstanceUpdates?.LastChangedBy
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildCreateElementsPayload(mutation.CreateDataElements)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildUpdateElementsPayload(mutation.UpdateDataElements)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildDeleteElementsPayload(mutation.DeleteDataElements)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildInstanceUpdatesPayload(mutation)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildEventsPayload(instanceGuid, mutation)
        );
        AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            BuildOutboxPayload(instanceGuid, mutation)
        );

        try
        {
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            bool replayed = false;
            IReadOnlyList<string> createdDataElementIds = [];
            InstanceInternal instance = await PgInstanceRepository.ReadInstanceResultAsync(
                reader,
                includeElements: true,
                cancellationToken,
                firstRowCallback: row =>
                {
                    replayed = row.GetBoolean(row.GetOrdinal("replayed"));
                    createdDataElementIds = ReadTextArray(row, "createddataelementids");
                }
            );

            if (instance is null)
            {
                throw new UnreachableException(
                    "Apply mutation function returned no instance rows."
                );
            }

            return new InstanceMutationApplyResult(replayed, createdDataElementIds, instance);
        }
        catch (PostgresException exception) when (exception.SqlState == "AM001")
        {
            // AM001 maps client-caused mutation failures to HTTP responses, so the repository deliberately stays quiet here.
            throw CreateApplyMutationException(instanceGuid, exception);
        }
        // RepositoryException is already the mapped/client-facing form; leave it unlogged for controllers to surface.
        catch (Exception exception) when (exception is not RepositoryException)
        {
            _logger?.LogError(
                exception,
                "Failed to apply aggregate mutation for instance {InstanceGuid}.",
                instanceGuid
            );
            throw;
        }
    }

    private static string BuildCreateElementsPayload(
        IReadOnlyList<DataElementInternal> dataElements
    )
    {
        if (dataElements is null || dataElements.Count == 0)
        {
            return null;
        }

        return BuildJsonPayload(writer =>
        {
            writer.WriteStartArray();
            foreach (DataElementInternal dataElement in dataElements)
            {
                if (string.IsNullOrEmpty(dataElement.Id))
                {
                    dataElement.Id = Guid.NewGuid().ToString();
                }

                if (dataElement.Created is { } created)
                {
                    dataElement.Created = NormalizePayloadTimestamp(created);
                }

                dataElement.LastChanged = null;
                dataElement.LastChangedBy = null;

                if (dataElement.DeleteStatus?.HardDeleted is { } hardDeleted)
                {
                    dataElement.DeleteStatus.HardDeleted = NormalizePayloadTimestamp(hardDeleted);
                }

                writer.WriteStartObject();
                writer.WriteString("elementId", dataElement.Id);
                writer.WritePropertyName("element");
                JsonSerializer.Serialize(writer, dataElement, OmitNullPropertiesJsonOptions);
                WriteBlobVersionProperty(writer, "blobVersion", dataElement.BlobVersionId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private static string BuildUpdateElementsPayload(
        IReadOnlyList<InstanceMutationDataElementUpdate> updates
    )
    {
        if (updates is null || updates.Count == 0)
        {
            return null;
        }

        return BuildJsonPayload(writer =>
        {
            writer.WriteStartArray();
            foreach (InstanceMutationDataElementUpdate update in updates)
            {
                BuildDataElementUpdatePayload(
                    update.Properties,
                    out DataElementInternal element,
                    out List<string> elementProperties,
                    out string newCurrentBlobVersion
                );

                if (element.DeleteStatus?.HardDeleted is { } hardDeleted)
                {
                    element.DeleteStatus.HardDeleted = NormalizePayloadTimestamp(hardDeleted);
                }

                writer.WriteStartObject();
                writer.WriteString("elementId", update.DataElementId.ToString());
                WriteRawJsonProperty(
                    writer,
                    "elementChanges",
                    CustomSerializer.Serialize(element, elementProperties)
                );
                WriteBlobVersionProperty(writer, "newBlobVersion", newCurrentBlobVersion);
                WriteBlobVersionProperty(
                    writer,
                    "expectedBlobVersion",
                    update.ExpectedCurrentBlobVersion
                );
                writer.WriteBoolean("ignoreLock", update.IgnoreLock);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private static string BuildDeleteElementsPayload(
        IReadOnlyList<InstanceMutationDataElementDelete> dataElements
    )
    {
        if (dataElements is null || dataElements.Count == 0)
        {
            return null;
        }

        return BuildJsonPayload(writer =>
        {
            writer.WriteStartArray();
            foreach (InstanceMutationDataElementDelete dataElement in dataElements)
            {
                writer.WriteStartObject();
                writer.WriteString("elementId", dataElement.DataElement.Id);
                writer.WriteBoolean("ignoreLock", dataElement.IgnoreLock);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private static string BuildInstanceUpdatesPayload(InstanceMutationCommit mutation)
    {
        List<string> instanceUpdateProperties =
            mutation
                .InstanceUpdateProperties?.Where(property =>
                    property != nameof(InstanceInternal.LastChanged)
                    && property != nameof(InstanceInternal.LastChangedBy)
                )
                .ToList()
            ?? [];
        if (instanceUpdateProperties.Count == 0)
        {
            return null;
        }

        InstanceInternal instance = mutation.InstanceUpdates;
        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.Created))
            && instance.Created is { } created
        )
        {
            instance.Created = NormalizePayloadTimestamp(created);
        }

        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.DueBefore))
            && instance.DueBefore is { } dueBefore
        )
        {
            instance.DueBefore = NormalizePayloadTimestamp(dueBefore);
        }

        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.VisibleAfter))
            && instance.VisibleAfter is { } visibleAfter
        )
        {
            instance.VisibleAfter = NormalizePayloadTimestamp(visibleAfter);
        }

        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.Status))
            && instance.Status is { } status
        )
        {
            if (status.Archived is { } archived)
            {
                status.Archived = NormalizePayloadTimestamp(archived);
            }

            if (status.SoftDeleted is { } softDeleted)
            {
                status.SoftDeleted = NormalizePayloadTimestamp(softDeleted);
            }

            if (status.HardDeleted is { } hardDeleted)
            {
                status.HardDeleted = NormalizePayloadTimestamp(hardDeleted);
            }
        }

        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.Process))
            && instance.Process is { } process
        )
        {
            if (process.Started is { } processStarted)
            {
                process.Started = NormalizePayloadTimestamp(processStarted);
            }

            if (process.Ended is { } processEnded)
            {
                process.Ended = NormalizePayloadTimestamp(processEnded);
            }

            if (process.CurrentTask?.Started is { } currentTaskStarted)
            {
                process.CurrentTask.Started = NormalizePayloadTimestamp(currentTaskStarted);
            }

            if (process.CurrentTask?.Ended is { } currentTaskEnded)
            {
                process.CurrentTask.Ended = NormalizePayloadTimestamp(currentTaskEnded);
            }
        }

        if (
            instanceUpdateProperties.Contains(nameof(InstanceInternal.CompleteConfirmations))
            && instance.CompleteConfirmations is not null
        )
        {
            foreach (CompleteConfirmation confirmation in instance.CompleteConfirmations)
            {
                confirmation.ConfirmedOn = NormalizePayloadTimestamp(confirmation.ConfirmedOn);
            }
        }

        return BuildJsonPayload(writer =>
            WriteInstanceUpdatePayloadItem(writer, instance, instanceUpdateProperties)
        );
    }

    private static void WriteInstanceUpdatePayloadItem(
        Utf8JsonWriter writer,
        InstanceInternal instance,
        List<string> updateProperties
    )
    {
        PgInstanceRepository.InstanceUpdateCommandArguments arguments =
            PgInstanceRepository.BuildUpdateCommandArguments(instance, updateProperties);

        writer.WriteStartObject();
        WriteRawJsonProperty(
            writer,
            "toplevelsimpleprops",
            arguments.TopLevelSimpleProperties,
            "{}"
        );
        WriteJsonProperty(writer, "datavalues", arguments.DataValues);
        WriteJsonProperty(writer, "completeconfirmations", arguments.CompleteConfirmations);
        WriteJsonProperty(writer, "presentationtexts", arguments.PresentationTexts);
        WriteJsonProperty(writer, "status", arguments.Status);
        WriteJsonProperty(writer, "substatus", arguments.Substatus);
        WriteJsonProperty(writer, "process", arguments.Process);
        WriteScalarJsonProperty(writer, "taskid", arguments.TaskId);
        WriteScalarJsonProperty(writer, "confirmed", arguments.Confirmed);
        writer.WriteEndObject();
    }

    private static string BuildEventsPayload(Guid instanceGuid, InstanceMutationCommit mutation)
    {
        if (mutation.InstanceEvents is null || mutation.InstanceEvents.Count == 0)
        {
            return null;
        }

        string instanceId = $"{mutation.InstanceUpdates.InstanceOwner.PartyId}/{instanceGuid}";
        foreach (InstanceEvent instanceEvent in mutation.InstanceEvents)
        {
            instanceEvent.Id ??= Guid.NewGuid();
            instanceEvent.InstanceId ??= instanceId;
            if (instanceEvent.Created is { } created)
            {
                instanceEvent.Created = NormalizePayloadTimestamp(created);
            }

            if (instanceEvent.ProcessInfo?.Started is { } processStarted)
            {
                instanceEvent.ProcessInfo.Started = NormalizePayloadTimestamp(processStarted);
            }

            if (instanceEvent.ProcessInfo?.Ended is { } processEnded)
            {
                instanceEvent.ProcessInfo.Ended = NormalizePayloadTimestamp(processEnded);
            }

            if (instanceEvent.ProcessInfo?.CurrentTask?.Started is { } currentTaskStarted)
            {
                instanceEvent.ProcessInfo.CurrentTask.Started = NormalizePayloadTimestamp(
                    currentTaskStarted
                );
            }

            if (instanceEvent.ProcessInfo?.CurrentTask?.Ended is { } currentTaskEnded)
            {
                instanceEvent.ProcessInfo.CurrentTask.Ended = NormalizePayloadTimestamp(
                    currentTaskEnded
                );
            }
        }

        return BuildJsonPayload(writer =>
            JsonSerializer.Serialize(writer, mutation.InstanceEvents)
        );
    }

    private string BuildOutboxPayload(Guid instanceGuid, InstanceMutationCommit mutation)
    {
        if (mutation.InstanceEvents is null || mutation.InstanceEvents.Count == 0)
        {
            return null;
        }

        InstanceEventType eventType = OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation(
            mutation.InstanceEvents
        );

        SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
            mutation.InstanceUpdates.AppId,
            mutation.InstanceUpdates.InstanceOwner.PartyId,
            instanceGuid.ToString(),
            mutation.InstanceUpdates.Created ?? DateTime.UtcNow,
            false,
            eventType
        );

        OutboxInsertRow row = _outboxInsertRowFactory.TryBuild(instanceUpdateCommand);
        if (row is null)
        {
            return null;
        }

        return BuildJsonPayload(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("appid", row.AppId);
            writer.WriteNumber("partyid", row.PartyId);
            writer.WriteNumber("delaySeconds", row.DelaySeconds);
            writer.WriteString("instancecreated", NormalizePayloadTimestamp(row.InstanceCreated));
            writer.WriteBoolean("ismigration", row.IsMigration);
            writer.WriteNumber("instanceeventtype", (int)row.InstanceEventType);
            writer.WriteEndObject();
        });
    }

    private static string BuildJsonPayload(Action<Utf8JsonWriter> writePayload)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writePayload(writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteRawJsonProperty(
        Utf8JsonWriter writer,
        string propertyName,
        string json,
        string fallbackJson = "null"
    )
    {
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(json ?? fallbackJson);
    }

    private static void WriteJsonProperty(Utf8JsonWriter writer, string propertyName, object value)
    {
        if (value is null or DBNull)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        if (value is string json)
        {
            writer.WriteRawValue(json);
            return;
        }

        JsonSerializer.Serialize(writer, value);
    }

    private static void WriteScalarJsonProperty(
        Utf8JsonWriter writer,
        string propertyName,
        object value
    )
    {
        writer.WritePropertyName(propertyName);
        if (value is null or DBNull)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value);
    }

    private static void WriteBlobVersionProperty(
        Utf8JsonWriter writer,
        string propertyName,
        string blobVersionId
    )
    {
        string decodedBlobVersion = ToDecodedBlobVersion(blobVersionId);
        if (decodedBlobVersion is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, decodedBlobVersion);
    }

    internal static void AddNullableParameter(
        NpgsqlParameterCollection parameters,
        NpgsqlDbType type,
        object value
    ) => parameters.AddWithValue(type, value ?? DBNull.Value);

    private static DateTime NormalizePayloadTimestamp(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Aggregate mutation payload timestamps must be UTC.",
                nameof(value)
            );
        }

        return new DateTime(
            (value.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
    }

    private async Task<InstanceInternal> ReadInstanceForReplay(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlCommand cmd = new(ReadInstanceSql, connection, transaction);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await PgInstanceRepository.ReadInstanceResultAsync(
            reader,
            includeElements: true,
            cancellationToken
        );
    }

    internal static IReadOnlyList<string> ReadTextArray(NpgsqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);
    }

    private static void EnsureReplaySnapshotMatchesAdmission(
        InstanceInternal instance,
        int admittedInstanceVersion,
        int admittedProcessStateVersion
    )
    {
        if (
            instance.Versions.InstanceVersion != admittedInstanceVersion
            || instance.Versions.ProcessStateVersion != admittedProcessStateVersion
        )
        {
            throw new InstanceVersionMismatchException(
                instance.Versions.InstanceVersion,
                instance.Versions.ProcessStateVersion
            );
        }
    }

    private static Exception CreateApplyMutationException(
        Guid instanceGuid,
        PostgresException exception
    )
    {
        ApplyMutationError error = ParseApplyMutationError(exception);
        return error.Code switch
        {
            "instance_not_found" => new RepositoryException(
                $"Instance {instanceGuid} was not found.",
                HttpStatusCode.NotFound
            ),
            "instance_version_mismatch" => new InstanceVersionMismatchException(
                RequireCurrentInstanceVersion(error, exception),
                RequireCurrentProcessStateVersion(error, exception)
            ),
            "idempotency_key_not_found" or "instance_already_advanced" =>
                new InstanceVersionMismatchException(
                    RequireCurrentInstanceVersion(error, exception),
                    RequireCurrentProcessStateVersion(error, exception)
                ),
            "process_state_version_mismatch" => new ProcessStateVersionMismatchException(
                RequireCurrentInstanceVersion(error, exception),
                RequireCurrentProcessStateVersion(error, exception)
            ),
            "idempotency_key_instance_mismatch" => new RepositoryException(
                "Idempotency key was already used for another instance.",
                HttpStatusCode.Conflict
            ),
            "data_element_not_found" => new RepositoryException(
                error.DataElementId is null
                    ? "Data element was not found."
                    : $"Data element {error.DataElementId} was not found.",
                HttpStatusCode.NotFound
            ),
            "instance_hard_deleted" => CreateInstanceHardDeletedException(instanceGuid),
            "data_element_hard_deleted" => new RepositoryException(
                error.DataElementId is null
                    ? "Data element is deleted and cannot be updated."
                    : $"Data element {error.DataElementId} is deleted and cannot be updated.",
                HttpStatusCode.NotFound
            ),
            "locked" => new RepositoryException(
                error.DataElementId is null
                    ? "Data element is locked and cannot be updated or deleted."
                    : $"Data element {error.DataElementId} is locked and cannot be updated or deleted.",
                HttpStatusCode.Conflict
            ),
            "blob_version_mismatch" => new DataElementBlobVersionMismatchException(
                error.DataElementId is null
                    ? "Data element current blob version did not match expected version."
                    : $"Data element {error.DataElementId} current blob version did not match expected version.",
                RequireCurrentInstanceVersion(error, exception),
                RequireCurrentProcessStateVersion(error, exception)
            ),
            _ => new UnreachableException(
                $"Unexpected aggregate mutation SQL error '{error.Code}'.",
                exception
            ),
        };
    }

    private static RepositoryException CreateInstanceHardDeletedException(Guid instanceGuid) =>
        new($"Instance {instanceGuid} is deleted and cannot be modified.", HttpStatusCode.NotFound);

    private static ApplyMutationError ParseApplyMutationError(PostgresException exception)
    {
        try
        {
            using JsonDocument message = JsonDocument.Parse(exception.MessageText);
            if (message.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw CreateApplyMutationContractException(
                    exception,
                    "Aggregate mutation SQL error MESSAGE was not a JSON object."
                );
            }

            return new ApplyMutationError(
                ReadRequiredString(message.RootElement, "code", exception),
                ReadNullableInt32(message.RootElement, "currentInstanceVersion", exception),
                ReadNullableInt32(message.RootElement, "currentProcessStateVersion", exception),
                ReadNullableString(message.RootElement, "dataElementId", exception)
            );
        }
        catch (JsonException)
        {
            throw CreateApplyMutationContractException(
                exception,
                "Aggregate mutation SQL error MESSAGE was not valid JSON."
            );
        }
    }

    private static string ReadRequiredString(
        JsonElement element,
        string propertyName,
        PostgresException exception
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString())
        )
        {
            throw CreateApplyMutationContractException(
                exception,
                $"Aggregate mutation SQL error MESSAGE was missing required property '{propertyName}'."
            );
        }

        return property.GetString();
    }

    private static int? ReadNullableInt32(
        JsonElement element,
        string propertyName,
        PostgresException exception
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
        {
            throw CreateApplyMutationContractException(
                exception,
                $"Aggregate mutation SQL error MESSAGE property '{propertyName}' was not an integer."
            );
        }

        return value;
    }

    private static string ReadNullableString(
        JsonElement element,
        string propertyName,
        PostgresException exception
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw CreateApplyMutationContractException(
                exception,
                $"Aggregate mutation SQL error MESSAGE property '{propertyName}' was not a string."
            );
        }

        return property.GetString();
    }

    private static int RequireCurrentInstanceVersion(
        ApplyMutationError error,
        PostgresException exception
    ) =>
        error.CurrentInstanceVersion
        ?? throw CreateApplyMutationContractException(
            exception,
            "Aggregate mutation SQL error MESSAGE was missing currentInstanceVersion."
        );

    private static int RequireCurrentProcessStateVersion(
        ApplyMutationError error,
        PostgresException exception
    ) =>
        error.CurrentProcessStateVersion
        ?? throw CreateApplyMutationContractException(
            exception,
            "Aggregate mutation SQL error MESSAGE was missing currentProcessStateVersion."
        );

    private static UnreachableException CreateApplyMutationContractException(
        PostgresException exception,
        string message
    ) => new(message, exception);

    private sealed record ApplyMutationError(
        string Code,
        int? CurrentInstanceVersion,
        int? CurrentProcessStateVersion,
        string DataElementId
    );

    private static void BuildDataElementUpdatePayload(
        Dictionary<string, object> propertylist,
        out DataElementInternal element,
        out List<string> elementProperties,
        out string blobVersionId
    )
    {
        const int allowedNumberOfProperties = 14;
        if (propertylist.Count > allowedNumberOfProperties)
        {
            throw new ArgumentOutOfRangeException(
                nameof(propertylist),
                $"PropertyList can contain at most {allowedNumberOfProperties} entries."
            );
        }

        elementProperties = [];
        element = new DataElementInternal();
        blobVersionId = null;

        foreach (var kvp in propertylist)
        {
            switch (kvp.Key)
            {
                case "/locked":
                    element.Locked = (bool)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Locked));
                    break;
                case "/refs":
                    element.Refs = (List<Guid>)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Refs));
                    break;
                case "/references":
                    element.References = (List<Reference>)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.References));
                    break;
                case "/tags":
                    element.Tags = (List<string>)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Tags));
                    break;
                case "/userDefinedMetadata":
                    element.UserDefinedMetadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.UserDefinedMetadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/metadata":
                    element.Metadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Metadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/deleteStatus":
                    element.DeleteStatus = (DeleteStatus)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.DeleteStatus));
                    break;
                case "/fileScanResult":
                    element.FileScanResult = (FileScanResult)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.FileScanResult));
                    break;
                case "/contentType":
                    element.ContentType = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.ContentType));
                    break;
                case "/filename":
                    element.Filename = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Filename));
                    break;
                case "/size":
                    element.Size = (long)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.Size));
                    break;
                case "/blobStoragePath":
                    element.BlobStoragePath = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.BlobStoragePath));
                    break;
                case "/isRead":
                    element.IsRead = (bool)kvp.Value;
                    elementProperties.Add(nameof(DataElementInternal.IsRead));
                    break;
                case "/currentBlobVersion":
                    blobVersionId = (string)kvp.Value;
                    break;
                default:
                    throw new ArgumentException("Unexpected key " + kvp.Key);
            }
        }
    }

    internal static string ToDecodedBlobVersion(string blobVersionId)
    {
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return null;
        }

        try
        {
            return BlobVersionId.Decode(blobVersionId).ToString();
        }
        catch (FormatException exception)
        {
            throw new RepositoryException(
                $"Blob version id '{blobVersionId}' is not valid.",
                exception,
                HttpStatusCode.BadRequest
            );
        }
    }
}
