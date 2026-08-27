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
/// Reads the blob version columns shared by the storage functions that return attached blob
/// versions grouped by the storage context they were written to.
/// </summary>
internal static class BlobVersionReferenceReader
{
    internal static async Task<BlobVersionReferencesInternal> ReadAsync(
        NpgsqlDataReader reader,
        string instanceGuidColumn = "instanceguid",
        string appIdColumn = "appid",
        string blobStorageOrgColumn = "blobstorageorg",
        string storageAccountNumberColumn = "storageaccountnumber",
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
        Guid[] blobVersions = await reader.GetFieldValueAsync<Guid[]>(
            blobVersionsColumn,
            cancellationToken
        );

        return new BlobVersionReferencesInternal(
            await reader.GetFieldValueAsync<Guid>(instanceGuidColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(appIdColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(blobStorageOrgColumn, cancellationToken),
            storageAccountNumber,
            blobVersions.Select(BlobVersionId.Encode)
        );
    }
}
