using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
    private readonly CapturingLoggerProvider _logs = new();
    private readonly StorageAccountResolver _resolver;

    public StorageAccountResolverTests()
    {
        _resolver = new StorageAccountResolver(
            _repository.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_azureConfiguration),
            Options.Create(new GeneralSettings()),
            _logs.CreateLogger<StorageAccountResolver>()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_StorageAccountCacheSecondsNotPositive_ThrowsWithMessage(int seconds)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new StorageAccountResolver(
                _repository.Object,
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(_azureConfiguration),
                Options.Create(new GeneralSettings { StorageAccountCacheSeconds = seconds }),
                _logs.CreateLogger<StorageAccountResolver>()
            )
        );

        Assert.Equal(
            $"GeneralSettings:StorageAccountCacheSeconds must be greater than zero, but is {seconds}.",
            exception.Message
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
    public async Task ConcurrentCacheMisses_ReadRowsOnce()
    {
        TaskCompletionSource<StorageAccountRows> read = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _repository.Setup(r => r.ReadAll(It.IsAny<CancellationToken>())).Returns(read.Task);

        Task<StorageAccount> byId = _resolver.GetById(5);
        Task<StorageAccount> forWrite = _resolver.ForWrite("ttd", 1);
        read.SetResult(Rows([Row(5, _namedAccount, "ttd", 1, writable: true)]));

        Assert.Equal(new StorageAccount(5, _namedAccount), await byId);
        Assert.Equal(new StorageAccount(5, _namedAccount), await forWrite);
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_MissingFromCache_RereadsRows()
    {
        _repository
            .SetupSequence(r => r.ReadAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows([Row(1, _namedAccount, "ttd", 1, writable: true)]))
            .ReturnsAsync(
                Rows([
                    Row(1, _namedAccount, "ttd", 1, writable: false),
                    Row(
                        2,
                        new AzureBlobProviderInfo("ttdaltinndevstrg02", "ttd-container"),
                        "ttd",
                        1,
                        writable: true
                    ),
                ])
            );
        await _resolver.GetById(1);

        StorageAccount account = await _resolver.GetById(2);

        Assert.Equal(
            new StorageAccount(2, new AzureBlobProviderInfo("ttdaltinndevstrg02", "ttd-container")),
            account
        );
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
            Row(1, new AzureBlobProviderInfo("old", "container"), "ttd", 1, writable: false),
            Row(2, _namedAccount, "ttd", 1, writable: true),
            Row(3, new AzureBlobProviderInfo("other", "container"), "ttd", 2, writable: true),
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

    [Fact]
    public async Task UnreadableRow_FailsOnlyOperationsThatUseIt()
    {
        SetupRows(
            [
                Row(1, _namedAccount, "ttd", 2, writable: true),
                Row(
                    2,
                    new AzureBlobProviderInfo("skdaltinndevstrg01", "skd-container"),
                    "skd",
                    1,
                    writable: true
                ),
            ],
            [Unreadable(7, "ttd", 1, writable: true), Unreadable(8, "ttd", 3, writable: false)]
        );

        InvalidOperationException byId = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _resolver.GetById(7)
        );
        InvalidOperationException forElement = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                _resolver.ForElement(VersionedElement(8, "ttd", 3), "ttd", 3)
        );
        InvalidOperationException forWrite = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                _resolver.ForWrite("ttd", 1)
        );

        Assert.Contains("Storage account 7 ", byId.Message);
        Assert.Contains("'unknown-provider'", byId.Message);
        Assert.Contains("The provider is not supported.", byId.Message);
        Assert.Contains("Storage account 8 ", forElement.Message);
        Assert.Contains("Storage account 7 ", forWrite.Message);
        Assert.Equal(new StorageAccount(1, _namedAccount), await _resolver.GetById(1));
        Assert.Equal(new StorageAccount(1, _namedAccount), await _resolver.ForWrite("ttd", 2));
        Assert.Equal(
            new StorageAccount(2, new AzureBlobProviderInfo("skdaltinndevstrg01", "skd-container")),
            await _resolver.ForWrite("skd", 1)
        );
        Assert.Equal(_resolver.PatternDefault("ttd", 3), await _resolver.ForWrite("ttd", 3));
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_UnreadableRow_LogsCriticalOnEveryRefresh()
    {
        SetupRows([], [Unreadable(7, "ttd", 1, writable: true)]);

        await _resolver.ForWrite("skd", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.GetById(9));

        IReadOnlyList<string> critical = _logs.Messages(LogLevel.Critical);
        Assert.Equal(2, critical.Count);
        Assert.All(critical, message => Assert.StartsWith("Storage account 7 ", message));
        _repository.Verify(r => r.ReadAll(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private void SetupRows(
        IReadOnlyList<StorageAccountRow> rows,
        IReadOnlyList<UnreadableStorageAccountRow>? unreadable = null
    )
    {
        _repository
            .Setup(r => r.ReadAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(rows, unreadable));
    }

    private static StorageAccountRows Rows(
        IReadOnlyList<StorageAccountRow> rows,
        IReadOnlyList<UnreadableStorageAccountRow>? unreadable = null
    ) => new(rows, unreadable ?? []);

    private static UnreadableStorageAccountRow Unreadable(
        short id,
        string org,
        int number,
        bool writable
    ) => new(id, "unknown-provider", org, number, writable, "The provider is not supported.");

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
