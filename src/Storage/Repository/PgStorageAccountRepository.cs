using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IStorageAccountRepository"/>.
/// </summary>
public class PgStorageAccountRepository(NpgsqlDataSource dataSource) : IStorageAccountRepository
{
    private const string _readAllSql = "select * from storage.readstorageaccounts()";
    private const string _insertSql = "select storage.insertstorageaccount($1, $2, $3, $4, $5)";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StorageAccountRow>> ReadAll(
        CancellationToken cancellationToken = default
    )
    {
        List<StorageAccountRow> rows = [];
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_readAllSql);
        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            short id = await reader.GetFieldValueAsync<short>("id", cancellationToken);
            string provider = await reader.GetFieldValueAsync<string>(
                "provider",
                cancellationToken
            );
            string document = await reader.GetFieldValueAsync<string>(
                "providerinfo",
                cancellationToken
            );
            rows.Add(
                new StorageAccountRow(
                    id,
                    DeserializeProviderInfo(id, provider, document),
                    await reader.GetFieldValueAsync<string>("org", cancellationToken),
                    await reader.GetFieldValueAsync<int>("storageaccountnumber", cancellationToken),
                    await reader.GetFieldValueAsync<bool>("writable", cancellationToken),
                    await reader.GetFieldValueAsync<DateTimeOffset>("created", cancellationToken)
                )
            );
        }

        return rows;
    }

    /// <inheritdoc/>
    public async Task<short> Insert(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_insertSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, providerInfo.Provider);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, providerInfo.Serialize());
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, storageAccountNumber);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, writable);

        object? id = await pgcom.ExecuteScalarAsync(cancellationToken);
        return (short)id!;
    }

    private static StorageAccountProviderInfo DeserializeProviderInfo(
        short id,
        string provider,
        string document
    )
    {
        try
        {
            return StorageAccountProviderInfo.Deserialize(provider, document);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Storage account {id} has provider '{provider}' with a provider info document this build cannot read: {exception.Message}",
                exception
            );
        }
    }
}
