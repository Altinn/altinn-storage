#nullable disable

using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Reads the storage location of a data element's current blob version from the columns
/// <c>storageaccountid</c>, <c>blobstorageorg</c> and <c>blobstorageaccountnumber</c>, returned by the
/// storage functions that join the current blob version row.
/// </summary>
internal static class BlobStorageLocationReader
{
    internal static async Task ReadAsync(
        NpgsqlDataReader reader,
        DataElementInternal element,
        CancellationToken cancellationToken
    )
    {
        int storageAccountIdOrdinal = reader.GetOrdinal("storageaccountid");
        int blobStorageOrgOrdinal = reader.GetOrdinal("blobstorageorg");
        int blobStorageAccountNumberOrdinal = reader.GetOrdinal("blobstorageaccountnumber");

        element.StorageAccountId = await reader.IsDBNullAsync(
            storageAccountIdOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<short>(storageAccountIdOrdinal, cancellationToken);
        element.BlobStorageOrg = await reader.IsDBNullAsync(
            blobStorageOrgOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<string>(blobStorageOrgOrdinal, cancellationToken);
        element.BlobStorageAccountNumber = await reader.IsDBNullAsync(
            blobStorageAccountNumberOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<int>(
                blobStorageAccountNumberOrdinal,
                cancellationToken
            );
        element.BlobStorageLoaded = true;
    }
}
