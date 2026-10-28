#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Models;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// PostgreSQL implementation of aggregate instance mutations.
/// </summary>
public sealed class PgInstanceMutationRepository(
    NpgsqlDataSource dataSource,
    OutboxInsertRowFactory outboxInsertRowFactory
) : IInstanceMutationRepository
{
    internal const string _applyMutationSql =
        "select * from storage.applyinstancemutation_v2($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)";
    private const string _getReplayResultSql =
        "select storage.tryreplayinstancemutation($1, $2, $3, $4, $5) as createddataelementids";
    private const string _readInstanceSql = "select * from storage.readinstance_v2($1)";
    private const string _deleteIdempotencyRecordsCreatedBeforeSql =
        "select storage.deleteinstancemutationidempotency($1, $2)";

    /// <summary>
    /// SQLSTATE raised by storage.raiseinstancemutationerror to report a refused mutation.
    /// </summary>
    private const string _applyMutationErrorSqlState = "AM001";

    /// <inheritdoc/>
    public async Task<InstanceMutationApplyResult> GetReplayResult(
        Guid instanceGuid,
        int expectedInstanceVersion,
        int currentInstanceVersion,
        int currentProcessStateVersion,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<string> createdDataElementIds;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(
            cancellationToken
        );
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken
        );

        await using (NpgsqlCommand cmd = new(_getReplayResultSql, connection, transaction))
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
            catch (PostgresException exception)
                when (exception.SqlState == _applyMutationErrorSqlState)
            {
                throw CreateApplyMutationException(instanceGuid, exception);
            }
        }

        InstanceInternal instance =
            await ReadInstanceForReplay(connection, transaction, instanceGuid, cancellationToken)
            ?? throw new UnreachableException(
                "Replay admission succeeded but follow-up instance read returned no result."
            );

        // Replay admission has already proved that this is the produced version for the original
        // mutation. The snapshot may therefore legitimately be hard-deleted by that mutation.
        EnsureReplaySnapshotMatchesAdmission(
            instance,
            currentInstanceVersion,
            currentProcessStateVersion
        );

        await transaction.CommitAsync(cancellationToken);

        return new InstanceMutationApplyResult(true, createdDataElementIds, instance);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteIdempotencyRecordsCreatedBefore(
        DateTime createdBeforeUtc,
        int batchSize = 10_000,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand cmd = dataSource.CreateCommand(
            _deleteIdempotencyRecordsCreatedBeforeSql
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, createdBeforeUtc);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, batchSize);

        int totalDeleted = 0;
        while (true)
        {
            int deleted = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            if (deleted == 0)
            {
                return totalDeleted;
            }

            totalDeleted += deleted;
        }
    }

    /// <inheritdoc/>
    public async Task<InstanceMutationApplyResult> Apply(
        Guid instanceGuid,
        long instanceInternalId,
        InstanceMutationCommit mutation,
        CancellationToken cancellationToken = default
    )
    {
        ValidateAssignedIds(mutation);
        InstanceMutationOutboxDelivery delivery = outboxInsertRowFactory.TryBuildMutationDelivery(
            mutation.InstanceEvents
        );
        await using NpgsqlCommand cmd = dataSource.CreateCommand(_applyMutationSql);
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
            MutationTimestamp.NormalizeForPostgres(mutation.Stamp.LastChanged)
        );
        AddNullableParameter(cmd.Parameters, NpgsqlDbType.Text, mutation.Stamp.LastChangedBy);
        AddJsonParameter(cmd.Parameters, mutation.CreateDataElements);
        AddJsonParameter(cmd.Parameters, mutation.UpdateDataElements);
        AddJsonParameter(cmd.Parameters, mutation.DeleteDataElements);
        AddJsonParameter(cmd.Parameters, mutation.InstanceChanges);
        AddJsonParameter(cmd.Parameters, mutation.InstanceEvents);
        AddJsonParameter(cmd.Parameters, delivery);

        try
        {
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            bool replayed = false;
            IReadOnlyList<string> createdDataElementIds = [];
            InstanceInternal instance =
                await InstanceResultReader.ReadAsync(
                    reader,
                    includeElements: true,
                    cancellationToken,
                    firstRowCallback: row =>
                    {
                        replayed = row.GetBoolean(row.GetOrdinal("replayed"));
                        createdDataElementIds = ReadTextArray(row, "createddataelementids");
                    }
                )
                ?? throw new UnreachableException(
                    "Apply mutation function returned no instance rows."
                );

            return new InstanceMutationApplyResult(replayed, createdDataElementIds, instance);
        }
        catch (PostgresException exception) when (exception.SqlState == _applyMutationErrorSqlState)
        {
            throw CreateApplyMutationException(instanceGuid, exception);
        }
    }

    private static void ValidateAssignedIds(InstanceMutationCommit mutation)
    {
        foreach (DataElementCreation create in mutation.CreateDataElements)
        {
            if (create.Element.Id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Created data elements must have an assigned ID.",
                    nameof(mutation)
                );
            }
        }

        foreach (var instanceEvent in mutation.InstanceEvents)
        {
            if (instanceEvent.Id is null || string.IsNullOrEmpty(instanceEvent.InstanceId))
            {
                throw new ArgumentException(
                    "Instance events must have an assigned ID and instance identity.",
                    nameof(mutation)
                );
            }
        }
    }

    private static void AddJsonParameter<T>(NpgsqlParameterCollection parameters, T value)
    {
        AddNullableParameter(parameters, NpgsqlDbType.Jsonb, InstanceMutationJson.Serialize(value));
    }

    internal static void AddNullableParameter(
        NpgsqlParameterCollection parameters,
        NpgsqlDbType type,
        object value
    ) => parameters.AddWithValue(type, value ?? DBNull.Value);

    private static async Task<InstanceInternal> ReadInstanceForReplay(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlCommand cmd = new(_readInstanceSql, connection, transaction);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await InstanceResultReader.ReadAsync(
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

    internal static void EnsureReplaySnapshotMatchesAdmission(
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

    internal static Exception CreateApplyMutationException(
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
            "process_status_conflict" => new ProcessStatusConflictException(
                RequireCurrentProcessStatus(error, exception)
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
                ReadNullableString(message.RootElement, "currentProcessStatus", exception),
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

    private static ProcessStatus RequireCurrentProcessStatus(
        ApplyMutationError error,
        PostgresException exception
    ) =>
        error.CurrentProcessStatus is { } currentProcessStatus
            ? ProcessStatusHelper.ParsePersistedStatus(currentProcessStatus)
            : throw CreateApplyMutationContractException(
                exception,
                "Aggregate mutation SQL error MESSAGE was missing currentProcessStatus."
            );

    private static UnreachableException CreateApplyMutationContractException(
        PostgresException exception,
        string message
    ) => new(message, exception);

    private sealed record ApplyMutationError(
        string Code,
        int? CurrentInstanceVersion,
        int? CurrentProcessStateVersion,
        string CurrentProcessStatus,
        string DataElementId
    );
}
