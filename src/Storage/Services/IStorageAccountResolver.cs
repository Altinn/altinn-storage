using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Resolves which storage account a blob is read from or written to.
/// </summary>
public interface IStorageAccountResolver
{
    /// <summary>
    /// Gets the storage account with the given row id.
    /// </summary>
    /// <param name="id">The storage account row id.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// No row has the id, or this build cannot read the row with the id.
    /// </exception>
    Task<StorageAccount> GetById(short id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the storage account holding the current blob of a data element.
    /// </summary>
    /// <param name="element">
    /// The data element, read by a repository call that loads the storage location of its current blob version.
    /// </param>
    /// <param name="org">The blob storage org used for an element without blob versions.</param>
    /// <param name="number">The storage account number used for an element without blob versions.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// The element has a blob version whose storage location was not loaded, or whose storage account
    /// row is missing or cannot be read.
    /// </exception>
    Task<StorageAccount> ForElement(
        DataElementInternal element,
        string org,
        int number,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the storage account a blob version was written to.
    /// </summary>
    /// <param name="row">The blob version.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    Task<StorageAccount> ForVersion(
        DataElementBlobVersion row,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the storage account a group of blob versions was written to.
    /// </summary>
    /// <param name="refs">The blob versions.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    Task<StorageAccount> ForVersion(
        BlobVersionReferencesInternal refs,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the storage account new blob versions for an org and storage account number are written to:
    /// the writable row for the pair, or the pattern default when there is none.
    /// </summary>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// This build cannot read the writable row for the pair.
    /// </exception>
    Task<StorageAccount> ForWrite(
        string org,
        int number,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the storage account the configured naming pattern gives for an org and storage account number.
    /// </summary>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <returns>The storage account, without an id.</returns>
    StorageAccount PatternDefault(string org, int number);
}
