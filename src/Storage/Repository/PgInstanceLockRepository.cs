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
/// Represents an implementation of <see cref="IInstanceLockRepository"/>.
/// </summary>
public class PgInstanceLockRepository(NpgsqlDataSource dataSource) : IInstanceLockRepository
{
    /// <inheritdoc/>
    public async Task<(AcquireLockResult Result, Guid? LockId)> TryAcquireLock(
        long instanceInternalId,
        int ttlSeconds,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        var id = Guid.CreateVersion7();

        await using var npgsqlCommand = dataSource.CreateCommand(
            "CALL storage.acquireinstancelock(@id, @instanceinternalid, @ttl, @lockedby, @result);"
        );
        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        npgsqlCommand.Parameters.AddWithValue(
            "instanceinternalid",
            NpgsqlDbType.Bigint,
            instanceInternalId
        );
        npgsqlCommand.Parameters.AddWithValue(
            "ttl",
            NpgsqlDbType.Interval,
            TimeSpan.FromSeconds(ttlSeconds)
        );
        npgsqlCommand.Parameters.AddWithValue("lockedby", NpgsqlDbType.Text, userId);

        var resultParam = new NpgsqlParameter("result", NpgsqlDbType.Text)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        npgsqlCommand.Parameters.Add(resultParam);

        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);

        var result = resultParam.Value?.ToString();
        return result switch
        {
            "ok" => (AcquireLockResult.Success, id),
            "lock_held" => (AcquireLockResult.LockAlreadyHeld, null),
            _ => throw new UnreachableException(),
        };
    }

    /// <inheritdoc/>
    public async Task<UpdateLockResult> TryUpdateLockExpiration(
        Guid lockId,
        long instanceInternalId,
        int ttlSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var npgsqlCommand = dataSource.CreateCommand(
            "CALL storage.updateinstancelock(@id, @instanceinternalid, @ttl, @result);"
        );

        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, lockId);
        npgsqlCommand.Parameters.AddWithValue(
            "instanceinternalid",
            NpgsqlDbType.Bigint,
            instanceInternalId
        );
        npgsqlCommand.Parameters.AddWithValue(
            "ttl",
            NpgsqlDbType.Interval,
            TimeSpan.FromSeconds(ttlSeconds)
        );

        var resultParam = new NpgsqlParameter("result", NpgsqlDbType.Text)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        npgsqlCommand.Parameters.Add(resultParam);

        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);

        var result = resultParam.Value?.ToString();
        return result switch
        {
            "ok" => UpdateLockResult.Success,
            "lock_not_found" => UpdateLockResult.LockNotFound,
            "lock_expired" => UpdateLockResult.LockExpired,
            _ => throw new UnreachableException(),
        };
    }

    /// <inheritdoc/>
    public async Task<InstanceLock?> Get(Guid lockId, CancellationToken cancellationToken = default)
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

        var instanceLock = new InstanceLock
        {
            Id = reader.GetFieldValue<Guid>("id"),
            InstanceInternalId = reader.GetFieldValue<long>("instanceinternalid"),
            LockedAt = reader.GetFieldValue<DateTimeOffset>("lockedat"),
            LockedUntil = reader.GetFieldValue<DateTimeOffset>("lockeduntil"),
            LockedBy = reader.GetFieldValue<string>("lockedby"),
        };

        return instanceLock;
    }
}
