using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.UnitTest.Mocks;

/// <summary>
/// Storage accounts and resolvers for tests, using the Azurite configuration from
/// <c>appsettings.unittest.json</c>.
/// </summary>
public static class TestStorageAccounts
{
    /// <summary>
    /// Gets the blob storage configuration of the unit tests.
    /// </summary>
    public static AzureStorageConfiguration AzureConfiguration =>
        new()
        {
            AccountName = "devstoreaccount1",
            AccountKey =
                "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
            BlobEndPoint = "http://127.0.0.1:10000/devstoreaccount1",
            OrgStorageAccount = "{0}altinndevstorage01",
            OrgStorageContainer = "{0}-dev-appsdata-blob-db",
        };

    /// <summary>
    /// Creates a resolver over the given repository, or over a repository without rows, with its own cache.
    /// </summary>
    /// <param name="repository">The storage account rows.</param>
    /// <param name="configuration">The naming pattern, or the unit test configuration.</param>
    public static StorageAccountResolver CreateResolver(
        IStorageAccountRepository? repository = null,
        AzureStorageConfiguration? configuration = null
    )
    {
        return new StorageAccountResolver(
            repository ?? new StorageAccountRepositoryMock(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(configuration ?? AzureConfiguration),
            Options.Create(new GeneralSettings()),
            NullLogger<StorageAccountResolver>.Instance
        );
    }

    /// <summary>
    /// Gets the pattern default storage account for an org and number.
    /// </summary>
    public static StorageAccount PatternDefault(string org, int number = 1)
    {
        return CreateResolver().PatternDefault(org, number);
    }
}
