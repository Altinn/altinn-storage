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
/// PostgreSQL implementation of <see cref="IActiveDataRequestRepository"/>.
/// </summary>
public class PgActiveDataRequestRepository(NpgsqlDataSource dataSource)
    : IActiveDataRequestRepository
{
    /// <inheritdoc/>
    public async Task<(BeginMutationStatus Status, long? RequestId)> BeginDataMutation(
        Guid instanceGuid,
        LockToken? lockToken,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        await using var cmd = dataSource.CreateCommand(
            "CALL storage.begindatamutation(@instanceguid, @lockid, @secrethash, @timeout, @result, @requestid);"
        );

        cmd.Parameters.AddWithValue("instanceguid", NpgsqlDbType.Uuid, instanceGuid);
        cmd.Parameters.AddWithValue(
            "lockid",
            NpgsqlDbType.Bigint,
            lockToken is not null ? lockToken.Id : DBNull.Value
        );
        cmd.Parameters.AddWithValue(
            "secrethash",
            NpgsqlDbType.Bytea,
            lockToken is not null ? SHA256.HashData(lockToken.Secret) : DBNull.Value
        );
        cmd.Parameters.AddWithValue("timeout", NpgsqlDbType.Interval, timeout);

        var resultParam = new NpgsqlParameter("result", NpgsqlDbType.Text)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        cmd.Parameters.Add(resultParam);

        var requestIdParam = new NpgsqlParameter("requestid", NpgsqlDbType.Bigint)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value,
        };
        cmd.Parameters.Add(requestIdParam);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        var result = resultParam.Value?.ToString();
        return result switch
        {
            "ok" => (BeginMutationStatus.Ok, (long)requestIdParam.Value!),
            "mutation_blocked" => (BeginMutationStatus.MutationBlocked, null),
            "instance_not_found" => (BeginMutationStatus.InstanceNotFound, null),
            _ => throw new UnreachableException(),
        };
    }

    /// <inheritdoc/>
    public async Task EndDataMutation(long requestId, CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand("CALL storage.enddatamutation(@requestid);");

        cmd.Parameters.AddWithValue("requestid", NpgsqlDbType.Bigint, requestId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
