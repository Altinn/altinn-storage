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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Resolves storage accounts from the cached <c>storage.storageaccounts</c> rows, falling back to the
/// configured naming pattern for blobs and pairs without a row.
/// </summary>
/// <remarks>
/// Rows are immutable except for the writable flag, so a cached row is always correct for reads. A stale
/// writable flag only delays write routing until the cache expires. A row this build cannot read is
/// logged as critical on every refresh and fails only the operations that use it: reads of blobs in the
/// account, and writes for its org and number while it is writable. Such operations never fall back to
/// the pattern default, which holds none of the account's blobs.
/// </remarks>
public sealed class StorageAccountResolver(
    IStorageAccountRepository repository,
    IMemoryCache memoryCache,
    IOptions<AzureStorageConfiguration> storageConfiguration,
    IOptions<GeneralSettings> generalSettings,
    ILogger<StorageAccountResolver> logger
) : IStorageAccountResolver
{
    private const string _cacheKey = "storage-accounts";

    private readonly AzureStorageConfiguration _storageConfiguration = storageConfiguration.Value;
    private readonly TimeSpan _cacheDuration = CacheDuration(
        generalSettings.Value.StorageAccountCacheSeconds
    );

    /// <inheritdoc/>
    public async Task<StorageAccount> GetById(
        short id,
        CancellationToken cancellationToken = default
    )
    {
        Snapshot snapshot = await GetSnapshot(cancellationToken);
        if (!snapshot.Contains(id))
        {
            snapshot = await Refresh(cancellationToken);
        }

        if (snapshot.ById.TryGetValue(id, out StorageAccount? account))
        {
            return account;
        }

        if (snapshot.UnreadableById.TryGetValue(id, out UnreadableStorageAccountRow? unreadable))
        {
            throw new InvalidOperationException(unreadable.Description);
        }

        throw new InvalidOperationException($"Storage account {id} does not exist.");
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
        if (snapshot.Writable.TryGetValue((org, number), out StorageAccount? account))
        {
            return account;
        }

        if (
            snapshot.UnreadableWritable.TryGetValue(
                (org, number),
                out UnreadableStorageAccountRow? unreadable
            )
        )
        {
            throw new InvalidOperationException(
                $"New blob versions for org {org} number {number} go to an unreadable storage account. {unreadable.Description}"
            );
        }

        return PatternDefault(org, number);
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

    private static TimeSpan CacheDuration(int seconds)
    {
        if (seconds <= 0)
        {
            throw new InvalidOperationException(
                $"GeneralSettings:StorageAccountCacheSeconds must be greater than zero, but is {seconds}."
            );
        }

        return TimeSpan.FromSeconds(seconds);
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
        StorageAccountRows rows = await repository.ReadAll(cancellationToken);
        foreach (UnreadableStorageAccountRow row in rows.Unreadable)
        {
            logger.LogUnreadableStorageAccount(row);
        }

        Snapshot snapshot = new(
            rows.Readable.ToDictionary(row => row.Id, ToAccount),
            rows.Readable.Where(row => row.Writable)
                .ToDictionary(row => (row.Org, row.StorageAccountNumber), ToAccount),
            rows.Unreadable.ToDictionary(row => row.Id),
            rows.Unreadable.Where(row => row.Writable)
                .ToDictionary(row => (row.Org, row.StorageAccountNumber))
        );
        memoryCache.Set(_cacheKey, snapshot, _cacheDuration);
        return snapshot;
    }

    private static StorageAccount ToAccount(StorageAccountRow row) => new(row.Id, row.ProviderInfo);

    private sealed record Snapshot(
        IReadOnlyDictionary<short, StorageAccount> ById,
        IReadOnlyDictionary<(string Org, int Number), StorageAccount> Writable,
        IReadOnlyDictionary<short, UnreadableStorageAccountRow> UnreadableById,
        IReadOnlyDictionary<
            (string Org, int Number),
            UnreadableStorageAccountRow
        > UnreadableWritable
    )
    {
        public bool Contains(short id) => ById.ContainsKey(id) || UnreadableById.ContainsKey(id);
    }
}
