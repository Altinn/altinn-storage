using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The repository for storage account rows.
/// </summary>
public interface IStorageAccountRepository
{
    /// <summary>
    /// Reads all storage account rows.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The rows ordered by id.</returns>
    Task<IReadOnlyList<StorageAccountRow>> ReadAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a storage account row. A writable row takes the writable flag over from the previous
    /// writable row for the same org and number.
    /// </summary>
    /// <param name="providerInfo">Where the account keeps its blobs.</param>
    /// <param name="org">The blob storage org the account serves.</param>
    /// <param name="storageAccountNumber">The application storage account number the account serves.</param>
    /// <param name="writable">Whether new blob versions for the org and number are written to the account.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The id of the new row.</returns>
    Task<short> Insert(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    );
}
