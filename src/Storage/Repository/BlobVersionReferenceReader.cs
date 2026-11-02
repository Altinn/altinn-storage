#nullable disable

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Reads the blob version columns shared by the storage functions that return blob versions
/// grouped by the storage context they were written to.
/// </summary>
internal static class BlobVersionReferenceReader
{
    internal static async Task<BlobVersionReferencesInternal> ReadAsync(
        NpgsqlDataReader reader,
        string instanceGuidColumn = "instanceguid",
        string appIdColumn = "appid",
        string blobStorageOrgColumn = "blobstorageorg",
        string storageAccountNumberColumn = "storageaccountnumber",
        string storageAccountIdColumn = "storageaccountid",
        string blobVersionsColumn = "blobversions",
        CancellationToken cancellationToken = default
    )
    {
        int storageAccountOrdinal = reader.GetOrdinal(storageAccountNumberColumn);
        int? storageAccountNumber = await reader.IsDBNullAsync(
            storageAccountOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<int>(storageAccountOrdinal, cancellationToken);
        int storageAccountIdOrdinal = reader.GetOrdinal(storageAccountIdColumn);
        short? storageAccountId = await reader.IsDBNullAsync(
            storageAccountIdOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<short>(storageAccountIdOrdinal, cancellationToken);
        Guid[] blobVersions = await reader.GetFieldValueAsync<Guid[]>(
            blobVersionsColumn,
            cancellationToken
        );

        return new BlobVersionReferencesInternal(
            await reader.GetFieldValueAsync<Guid>(instanceGuidColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(appIdColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(blobStorageOrgColumn, cancellationToken),
            storageAccountNumber,
            storageAccountId,
            blobVersions.Select(BlobVersionId.Encode)
        );
    }
}
