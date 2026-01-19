#nullable enable

using System;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
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
    private const int _lockSecretSizeBytes = 20;

    /// <inheritdoc/>
    public async Task<(AcquireLockResult Result, LockToken? lockToken)> TryAcquireLock(
        long instanceInternalId,
        int ttlSeconds,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        var lockSecret = RandomNumberGenerator.GetBytes(_lockSecretSizeBytes);
        var lockSecretHash = SHA256.HashData(lockSecret);

        await using var npgsqlCommand = dataSource.CreateCommand(
            "CALL storage.acquireinstancelock(@instanceinternalid, @ttl, @lockedby, @secrethash, @result, @id);"
        );
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
        npgsqlCommand.Parameters.AddWithValue("secrethash", NpgsqlDbType.Bytea, lockSecretHash);

        var resultParam = new NpgsqlParameter("result", NpgsqlDbType.Text)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        npgsqlCommand.Parameters.Add(resultParam);

        var idParam = new NpgsqlParameter("id", NpgsqlDbType.Bigint)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        npgsqlCommand.Parameters.Add(idParam);

        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);

        var result = resultParam.Value?.ToString();
        return result switch
        {
            "ok" => (AcquireLockResult.Success, new LockToken((long)idParam.Value!, lockSecret)),
            "lock_held" => (AcquireLockResult.LockAlreadyHeld, null),
            _ => throw new UnreachableException(),
        };
    }

    /// <inheritdoc/>
    public async Task<UpdateLockResult> TryUpdateLockExpiration(
        LockToken lockToken,
        long instanceInternalId,
        int ttlSeconds,
        CancellationToken cancellationToken = default
    )
    {
        var lockSecretHash = SHA256.HashData(lockToken.Secret);

        await using var npgsqlCommand = dataSource.CreateCommand(
            "CALL storage.updateinstancelock(@id, @instanceinternalid, @ttl, @secrethash, @result);"
        );

        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, lockToken.Id);
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
        npgsqlCommand.Parameters.AddWithValue("secrethash", NpgsqlDbType.Bytea, lockSecretHash);

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
            "token_mismatch" => UpdateLockResult.TokenMismatch,
            _ => throw new UnreachableException(),
        };
    }

    /// <inheritdoc/>
    public async Task<InstanceLock?> Get(long lockId, CancellationToken cancellationToken = default)
    {
        await using var npgsqlCommand = dataSource.CreateCommand(
            """
            SELECT
                id,
                instanceinternalid,
                lockedat,
                lockeduntil,
                secrethash,
                lockedby
            FROM storage.instancelocks
            WHERE id = @id;
            """
        );
        npgsqlCommand.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, lockId);

        await using NpgsqlDataReader reader = await npgsqlCommand.ExecuteReaderAsync(
            cancellationToken
        );

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var instanceLock = new InstanceLock
        {
            Id = reader.GetFieldValue<long>("id"),
            InstanceInternalId = reader.GetFieldValue<long>("instanceinternalid"),
            LockedAt = reader.GetFieldValue<DateTimeOffset>("lockedat"),
            LockedUntil = reader.GetFieldValue<DateTimeOffset>("lockeduntil"),
            SecretHash = reader.GetFieldValue<byte[]>("secrethash"),
            LockedBy = reader.GetFieldValue<string>("lockedby"),
        };

        return instanceLock;
    }
}
