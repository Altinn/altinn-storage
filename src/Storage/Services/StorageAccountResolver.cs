using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Resolves storage accounts from the cached <c>storage.storageaccounts</c> rows, falling back to the
/// configured naming pattern for blobs and pairs without a row.
/// </summary>
/// <remarks>
/// Rows are immutable except for the writable flag, so a cached row is always correct for reads. A stale
/// writable flag only delays write routing until the cache expires.
/// </remarks>
public sealed class StorageAccountResolver(
    IStorageAccountRepository repository,
    IMemoryCache memoryCache,
    IOptions<AzureStorageConfiguration> storageConfiguration,
    IOptions<StorageAccountSettings> settings
) : IStorageAccountResolver
{
    private const string _cacheKey = "storage-accounts";

    private readonly AzureStorageConfiguration _storageConfiguration = storageConfiguration.Value;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(settings.Value.CacheSeconds);

    /// <inheritdoc/>
    public async Task<StorageAccount> GetById(
        short id,
        CancellationToken cancellationToken = default
    )
    {
        Snapshot snapshot = await GetSnapshot(cancellationToken);
        if (!snapshot.ById.TryGetValue(id, out StorageAccount? account))
        {
            snapshot = await Refresh(cancellationToken);
            if (!snapshot.ById.TryGetValue(id, out account))
            {
                throw new InvalidOperationException($"Storage account {id} does not exist.");
            }
        }

        return account;
    }

    /// <inheritdoc/>
    public async Task<StorageAccount> ForElement(
        DataElementInternal element,
        string org,
        int number,
        CancellationToken cancellationToken = default
    )
    {
        if (element.BlobVersionId is null)
        {
            return PatternDefault(org, number);
        }

        if (!element.BlobStorageLoaded)
        {
            throw new InvalidOperationException(
                $"The storage location of data element {element.Id} was not loaded with the element."
            );
        }

        if (element.StorageAccountId is short id)
        {
            return await GetById(id, cancellationToken);
        }

        if (element.BlobStorageOrg is null)
        {
            throw new InvalidOperationException(
                $"The current blob version {element.BlobVersionId} of data element {element.Id} does not exist."
            );
        }

        return PatternDefault(
            element.BlobStorageOrg,
            element.BlobStorageAccountNumber ?? StorageAccountNaming.DefaultStorageAccountNumber
        );
    }

    /// <inheritdoc/>
    public Task<StorageAccount> ForVersion(
        DataElementBlobVersion row,
        CancellationToken cancellationToken = default
    )
    {
        return ForVersion(
            row.StorageAccountId,
            row.BlobStorageOrg,
            row.StorageAccountNumber,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public Task<StorageAccount> ForVersion(
        BlobVersionReferencesInternal refs,
        CancellationToken cancellationToken = default
    )
    {
        return ForVersion(
            refs.StorageAccountId,
            refs.BlobStorageOrg,
            refs.StorageAccountNumber,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public async Task<StorageAccount> ForWrite(
        string org,
        int number,
        CancellationToken cancellationToken = default
    )
    {
        Snapshot snapshot = await GetSnapshot(cancellationToken);
        return snapshot.Writable.TryGetValue((org, number), out StorageAccount? account)
            ? account
            : PatternDefault(org, number);
    }

    /// <inheritdoc/>
    public StorageAccount PatternDefault(string org, int number)
    {
        return new StorageAccount(
            null,
            StorageAccountNaming.PatternDefault(_storageConfiguration, org, number)
        );
    }

    private async Task<StorageAccount> ForVersion(
        short? storageAccountId,
        string blobStorageOrg,
        int? storageAccountNumber,
        CancellationToken cancellationToken
    )
    {
        if (storageAccountId is short id)
        {
            return await GetById(id, cancellationToken);
        }

        return PatternDefault(
            blobStorageOrg,
            storageAccountNumber ?? StorageAccountNaming.DefaultStorageAccountNumber
        );
    }

    private async Task<Snapshot> GetSnapshot(CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(_cacheKey, out Snapshot? snapshot) && snapshot is not null)
        {
            return snapshot;
        }

        return await Refresh(cancellationToken);
    }

    private async Task<Snapshot> Refresh(CancellationToken cancellationToken)
    {
        IReadOnlyList<StorageAccountRow> rows = await repository.ReadAll(cancellationToken);
        Snapshot snapshot = new(
            rows.ToDictionary(row => row.Id, ToAccount),
            rows.Where(row => row.Writable)
                .ToDictionary(row => (row.Org, row.StorageAccountNumber), ToAccount)
        );
        memoryCache.Set(_cacheKey, snapshot, _cacheDuration);
        return snapshot;
    }

    private static StorageAccount ToAccount(StorageAccountRow row) => new(row.Id, row.ProviderInfo);

    private sealed record Snapshot(
        IReadOnlyDictionary<short, StorageAccount> ById,
        IReadOnlyDictionary<(string Org, int Number), StorageAccount> Writable
    );
}
