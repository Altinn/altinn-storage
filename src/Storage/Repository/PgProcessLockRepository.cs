#nullable enable

using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IProcessLockRepository"/>.
/// </summary>
public class PgProcessLockRepository(NpgsqlDataSource dataSource, TimeProvider timeProvider)
    : IProcessLockRepository
{
    /// <inheritdoc/>
    public async Task<Guid?> TryAcquireLock(
        long instanceInternalId,
        int ttlSeconds,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now.AddSeconds(ttlSeconds);

        await using var npgsqlCommand = dataSource.CreateCommand(
            """
            WITH lock_attempt AS (
                SELECT pg_try_advisory_xact_lock(@instance_internal_id) AS acquired
            )
            INSERT INTO storage.instancelocks (id, instanceinternalid, lockedat, lockeduntil, lockedby)
            SELECT @id, @instance_internal_id, @now, @locked_until, @locked_by
            FROM lock_attempt
            WHERE acquired = TRUE
            AND NOT EXISTS (
                SELECT 1 FROM storage.instancelocks
                WHERE instanceinternalid = @instance_internal_id
                AND lockeduntil > @now
            );
            """
        );
        npgsqlCommand.Parameters.AddWithValue(
            "instance_internal_id",
            NpgsqlDbType.Bigint,
            instanceInternalId
        );
        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        npgsqlCommand.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        npgsqlCommand.Parameters.AddWithValue(
            "locked_until",
            NpgsqlDbType.TimestampTz,
            lockedUntil
        );
        npgsqlCommand.Parameters.AddWithValue("locked_by", NpgsqlDbType.Text, userId);

        var rowsAffected = await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected != 1 && rowsAffected != 0)
        {
            throw new UnreachableException();
        }

        if (rowsAffected == 0)
        {
            return null;
        }

        return id;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateLockExpiration(
        Guid lockId,
        int ttlSeconds,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now.AddSeconds(ttlSeconds);

        await using var npgsqlCommand = dataSource.CreateCommand(
            """
            UPDATE storage.instancelocks
            SET lockeduntil = @locked_until
            WHERE id = @id
            AND lockeduntil > @now;
            """
        );
        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, lockId);
        npgsqlCommand.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        npgsqlCommand.Parameters.AddWithValue(
            "locked_until",
            NpgsqlDbType.TimestampTz,
            lockedUntil
        );

        var rowsAffected = await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected != 1 && rowsAffected != 0)
        {
            throw new UnreachableException();
        }

        return rowsAffected > 0;
    }

    /// <inheritdoc/>
    public async Task<ProcessLock?> Get(Guid lockId, CancellationToken cancellationToken = default)
    {
        await using var npgsqlCommand = dataSource.CreateCommand(
            """
            SELECT
                id,
                instanceinternalid,
                lockedat,
                lockeduntil,
                lockedby
            FROM storage.instancelocks
            WHERE id = @id;
            """
        );
        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, lockId);

        await using NpgsqlDataReader reader = await npgsqlCommand.ExecuteReaderAsync(
            cancellationToken
        );

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var processLock = new ProcessLock
        {
            Id = reader.GetFieldValue<Guid>("id"),
            InstanceInternalId = reader.GetFieldValue<long>("instanceinternalid"),
            LockedAt = reader.GetFieldValue<DateTimeOffset>("lockedat"),
            LockedUntil = reader.GetFieldValue<DateTimeOffset>("lockeduntil"),
            LockedBy = reader.GetFieldValue<string>("lockedby"),
        };

        return processLock;
    }
}
