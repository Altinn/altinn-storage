using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class StorageAccountResolverTests
{
    private static readonly AzureStorageConfiguration _azureConfiguration = new()
    {
        AccountName = "altinnstorage",
        OrgStorageAccount = "{0}altinndevstorage01",
        OrgStorageContainer = "{0}-dev-appsdata-blob-db",
    };

    private static readonly AzureBlobProviderInfo _namedAccount = new(
        "ttdnamedaccount",
        "ttd-named-container"
    );

    private readonly Mock<IStorageAccountRepository> _repository = new();
    private readonly StorageAccountResolver _resolver;

    public StorageAccountResolverTests()
    {
        _resolver = new StorageAccountResolver(
            _repository.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_azureConfiguration),
            Options.Create(new StorageAccountSettings())
        );
    }

    [Fact]
    public async Task GetById_ReadsRowsOnceAndServesFromCache()
    {
        SetupRows([Row(5, _namedAccount, "ttd", 1, writable: true)]);

        StorageAccount first = await _resolver.GetById(5);
        StorageAccount second = await _resolver.GetById(5);

        Assert.Equal(new StorageAccount(5, _namedAccount), first);
        Assert.Equal(first, second);
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_MissingFromCache_RereadsRows()
    {
        _repository
            .SetupSequence(r => r.ReadAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(1, _namedAccount, "ttd", 1, writable: true)])
            .ReturnsAsync([
                Row(1, _namedAccount, "ttd", 1, writable: false),
                Row(2, new FileSystemProviderInfo("/data/ttd"), "ttd", 1, writable: true),
            ]);
        await _resolver.GetById(1);

        StorageAccount account = await _resolver.GetById(2);

        Assert.Equal(new StorageAccount(2, new FileSystemProviderInfo("/data/ttd")), account);
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetById_MissingAfterReread_Throws()
    {
        SetupRows([Row(1, _namedAccount, "ttd", 1, writable: true)]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                _resolver.GetById(9)
        );

        Assert.Contains("9", exception.Message);
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ForElement_WithStorageAccountId_ReturnsRow()
    {
        SetupRows([Row(3, _namedAccount, "ttd", 1, writable: false)]);
        DataElementInternal element = VersionedElement(3, "ttd", 1);

        StorageAccount account = await _resolver.ForElement(element, "other", 9);

        Assert.Equal(new StorageAccount(3, _namedAccount), account);
    }

    [Theory]
    [InlineData(null, "ttdaltinndevstorage01")]
    [InlineData(1, "ttdaltinndevstorage01")]
    [InlineData(4, "ttdaltinndevstorage04")]
    public async Task ForElement_VersionWithoutStorageAccountId_UsesPatternFromVersionRow(
        int? versionNumber,
        string expectedAccountName
    )
    {
        DataElementInternal element = VersionedElement(null, "ttd", versionNumber);

        StorageAccount account = await _resolver.ForElement(element, "other", 9);

        Assert.Equal(
            new StorageAccount(
                null,
                new AzureBlobProviderInfo(expectedAccountName, "ttd-dev-appsdata-blob-db")
            ),
            account
        );
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForElement_LegacyElement_UsesPatternFromGivenOrgAndNumber()
    {
        DataElementInternal element = new() { Id = Guid.NewGuid() };

        StorageAccount account = await _resolver.ForElement(element, "ttd", 2);

        Assert.Equal(
            new StorageAccount(
                null,
                new AzureBlobProviderInfo("ttdaltinndevstorage02", "ttd-dev-appsdata-blob-db")
            ),
            account
        );
    }

    [Fact]
    public async Task ForElement_VersionedElementWithoutLoadedLocation_Throws()
    {
        DataElementInternal element = new()
        {
            Id = Guid.NewGuid(),
            BlobVersionId = "EREREREREREREREREREREQ",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _resolver.ForElement(element, "ttd", 1)
        );
    }

    [Fact]
    public async Task ForVersion_RowWithAndWithoutStorageAccountId()
    {
        SetupRows([Row(3, _namedAccount, "ttd", 1, writable: true)]);
        DataElementBlobVersion withId = new()
        {
            AppId = "ttd/app",
            BlobStorageOrg = "ttd",
            StorageAccountNumber = 1,
            StorageAccountId = 3,
        };
        DataElementBlobVersion withoutId = withId with
        {
            StorageAccountId = null,
            StorageAccountNumber = null,
        };

        Assert.Equal(new StorageAccount(3, _namedAccount), await _resolver.ForVersion(withId));
        Assert.Equal(_resolver.PatternDefault("ttd", 1), await _resolver.ForVersion(withoutId));
    }

    [Fact]
    public async Task ForVersion_ReferencesWithAndWithoutStorageAccountId()
    {
        SetupRows([Row(3, _namedAccount, "ttd", 1, writable: true)]);
        BlobVersionReferencesInternal withId = new(Guid.NewGuid(), "ttd/app", "ttd", 1, 3, []);
        BlobVersionReferencesInternal withoutId = new(
            Guid.NewGuid(),
            "ttd/app",
            "ttd",
            5,
            null,
            []
        );

        Assert.Equal(new StorageAccount(3, _namedAccount), await _resolver.ForVersion(withId));
        Assert.Equal(_resolver.PatternDefault("ttd", 5), await _resolver.ForVersion(withoutId));
    }

    [Fact]
    public async Task ForWrite_WritableRowForPair_ReturnsRow()
    {
        SetupRows([
            Row(1, new AzureBlobProviderInfo("old", "c"), "ttd", 1, writable: false),
            Row(2, _namedAccount, "ttd", 1, writable: true),
            Row(3, new AzureBlobProviderInfo("other", "c"), "ttd", 2, writable: true),
        ]);

        StorageAccount account = await _resolver.ForWrite("ttd", 1);

        Assert.Equal(new StorageAccount(2, _namedAccount), account);
    }

    [Fact]
    public async Task ForWrite_NoWritableRowForPair_ReturnsPatternDefault()
    {
        SetupRows([
            Row(1, _namedAccount, "ttd", 1, writable: false),
            Row(2, _namedAccount, "skd", 1, writable: true),
        ]);

        StorageAccount account = await _resolver.ForWrite("ttd", 1);

        Assert.Equal(
            new StorageAccount(
                null,
                new AzureBlobProviderInfo("ttdaltinndevstorage01", "ttd-dev-appsdata-blob-db")
            ),
            account
        );
    }

    private void SetupRows(IReadOnlyList<StorageAccountRow> rows)
    {
        _repository.Setup(r => r.ReadAll(It.IsAny<CancellationToken>())).ReturnsAsync(rows);
    }

    private static StorageAccountRow Row(
        short id,
        StorageAccountProviderInfo providerInfo,
        string org,
        int number,
        bool writable
    ) => new(id, providerInfo, org, number, writable, DateTimeOffset.UtcNow);

    private static DataElementInternal VersionedElement(
        short? storageAccountId,
        string org,
        int? number
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            BlobVersionId = "EREREREREREREREREREREQ",
            StorageAccountId = storageAccountId,
            BlobStorageOrg = org,
            BlobStorageAccountNumber = number,
            BlobStorageLoaded = true,
        };
}
