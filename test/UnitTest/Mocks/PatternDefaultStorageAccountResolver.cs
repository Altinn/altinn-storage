using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Services;

namespace Altinn.Platform.Storage.UnitTest.Mocks;

/// <summary>
/// Resolves every blob to the pattern default storage account, for tests with mocked repositories
/// that do not load the storage location of data elements.
/// </summary>
public class PatternDefaultStorageAccountResolver : IStorageAccountResolver
{
    private readonly IStorageAccountResolver _patternResolver =
        TestStorageAccounts.CreateResolver();

    public Task<StorageAccount> GetById(short id, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Storage account {id} does not exist.");

    public Task<StorageAccount> ForElement(
        DataElementInternal element,
        string org,
        int number,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            PatternDefault(
                element.BlobStorageOrg ?? org,
                element.BlobStorageAccountNumber ?? number
            )
        );

    public Task<StorageAccount> ForVersion(
        DataElementBlobVersion row,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            PatternDefault(
                row.BlobStorageOrg,
                row.StorageAccountNumber ?? StorageAccountNaming.DefaultStorageAccountNumber
            )
        );

    public Task<StorageAccount> ForVersion(
        BlobVersionReferencesInternal refs,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            PatternDefault(
                refs.BlobStorageOrg,
                refs.StorageAccountNumber ?? StorageAccountNaming.DefaultStorageAccountNumber
            )
        );

    public Task<StorageAccount> ForWrite(
        string org,
        int number,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(PatternDefault(org, number));

    public StorageAccount PatternDefault(string org, int number) =>
        _patternResolver.PatternDefault(org, number);
}
