using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class BlobRepositoryTests
{
    [Fact]
    public void GetVersionedBlobPath_WithVersionId_UsesDataElementsPath()
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();

        string result = BlobRepository.GetVersionedBlobPath("ttd/app", instanceGuid, blobVersionId);

        Assert.Equal($"ttd/app/{instanceGuid}/data-elements/{blobVersionId}", result);
    }
}

public class BlobRepositoryAzuriteTests(BlobRepositoryAzuriteFixture fixture)
    : IClassFixture<BlobRepositoryAzuriteFixture>
{

    [Fact]
    public async Task WriteBlob_ThenReadBlob_RoundtripsContent()
    {
        string expectedContent = $"content-{Guid.NewGuid():N}";
        string blobStoragePath = fixture.NewBlobPath("data-elements/version-1");

        await using MemoryStream upload = new(Encoding.UTF8.GetBytes(expectedContent));
        (long contentLength, DateTimeOffset lastModified) = await fixture.Repository.WriteBlob(
            BlobRepositoryAzuriteFixture.Org,
            upload,
            blobStoragePath,
            null
        );

        using Stream downloaded = await fixture.Repository.ReadBlob(
            BlobRepositoryAzuriteFixture.Org,
            blobStoragePath,
            null
        );
        using StreamReader reader = new(downloaded, Encoding.UTF8);

        Assert.Equal(Encoding.UTF8.GetByteCount(expectedContent), contentLength);
        Assert.NotEqual(default, lastModified);
        Assert.Equal(expectedContent, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadBlob_MissingBlob_ReturnsNull()
    {
        Stream result = await fixture.Repository.ReadBlob(
            BlobRepositoryAzuriteFixture.Org,
            fixture.NewBlobPath("missing"),
            null
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteBlob_ExistingThenMissing_IsIdempotent()
    {
        string blobStoragePath = fixture.NewBlobPath("data-elements/version-delete");
        await fixture.UploadText(blobStoragePath, "delete me");

        bool firstDelete = await fixture.Repository.DeleteBlob(
            BlobRepositoryAzuriteFixture.Org,
            blobStoragePath,
            null
        );
        bool secondDelete = await fixture.Repository.DeleteBlob(
            BlobRepositoryAzuriteFixture.Org,
            blobStoragePath,
            null
        );

        Assert.True(firstDelete);
        Assert.False(secondDelete);
        Assert.False(await fixture.Exists(blobStoragePath));
    }

    [Fact]
    public async Task DeleteDataBlobs_DeletesTargetInstancePrefixOnly()
    {
        string targetInstanceGuid = Guid.NewGuid().ToString();
        string otherInstanceGuid = Guid.NewGuid().ToString();
        string firstTargetBlob = $"ttd/app/{targetInstanceGuid}/data-elements/version-1";
        string secondTargetBlob = $"ttd/app/{targetInstanceGuid}/data/legacy";
        string otherInstanceBlob = $"ttd/app/{otherInstanceGuid}/data-elements/version-2";

        await fixture.UploadText(firstTargetBlob, "first target");
        await fixture.UploadText(secondTargetBlob, "second target");
        await fixture.UploadText(otherInstanceBlob, "other instance");

        bool result = await fixture.Repository.DeleteDataBlobs(
            BlobRepositoryAzuriteFixture.Org,
            "ttd/app",
            new Guid(targetInstanceGuid),
            null
        );

        Assert.True(result);
        Assert.False(await fixture.Exists(firstTargetBlob));
        Assert.False(await fixture.Exists(secondTargetBlob));
        Assert.True(await fixture.Exists(otherInstanceBlob));
    }

    [Fact]
    public async Task DeleteBlobsIfExists_ExistingMissingAndDuplicatePaths_ReturnsIndexedResults()
    {
        string existingBlob = fixture.NewBlobPath("data-elements/per-path-existing");
        string missingBlob = fixture.NewBlobPath("data-elements/per-path-missing");
        await fixture.UploadText(existingBlob, "delete me");

        bool[] result = await fixture.Repository.DeleteBlobsIfExists(
            BlobRepositoryAzuriteFixture.Org,
            [existingBlob, missingBlob, existingBlob],
            null
        );

        Assert.Equal([true, true, true], result);
        Assert.False(await fixture.Exists(existingBlob));
    }

    [Fact]
    public async Task DeleteBlobsIfExists_PerBlobFailure_ReturnsOnlySafePositions()
    {
        string existingBlob = fixture.NewBlobPath("data-elements/per-path-existing");
        string leasedBlob = fixture.NewBlobPath("data-elements/per-path-leased");
        string missingBlob = fixture.NewBlobPath("data-elements/per-path-missing");
        await fixture.UploadText(existingBlob, "delete me");
        await fixture.UploadText(leasedBlob, "keep me leased");
        BlobLeaseClient leaseClient = await fixture.AcquireLease(leasedBlob);

        try
        {
            bool[] result = await fixture.Repository.DeleteBlobsIfExists(
                BlobRepositoryAzuriteFixture.Org,
                [existingBlob, leasedBlob, missingBlob],
                null
            );

            Assert.Equal([true, false, true], result);
            Assert.False(await fixture.Exists(existingBlob));
            Assert.True(await fixture.Exists(leasedBlob));
        }
        finally
        {
            await leaseClient.ReleaseAsync();
        }
    }
}

public sealed class BlobRepositoryAzuriteFixture : IAsyncLifetime
{
    public const string Org = "ttd";

    private const string _accountName = "devstoreaccount1";
    private const string _accountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private static readonly string _blobEndPoint =
        Environment.GetEnvironmentVariable("ALTINN_STORAGE_AZURITE_BLOB_ENDPOINT")
        ?? "http://127.0.0.1:10000/devstoreaccount1";
    private readonly string _containerName = $"blobrepo-{Guid.NewGuid():N}"[..21];
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private BlobContainerClient _container = null!;

    public BlobRepositoryAzuriteFixture()
    {
        AzureStorageConfiguration configuration = new()
        {
            AccountName = _accountName,
            AccountKey = _accountKey,
            BlobEndPoint = _blobEndPoint,
            OrgStorageAccount = _accountName,
            OrgStorageContainer = $"{_containerName}-{{0}}",
        };

        Repository = new BlobRepository(
            _memoryCache,
            Options.Create(configuration),
            NullLogger<BlobRepository>.Instance
        );

        ContainerName = string.Format(configuration.OrgStorageContainer, Org);
    }

    public BlobRepository Repository { get; }

    public string ContainerName { get; }

    public async Task InitializeAsync()
    {
        StorageSharedKeyCredential storageCredentials = new(_accountName, _accountKey);
        BlobServiceClient blobServiceClient = new(new Uri(_blobEndPoint), storageCredentials);

        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
        await _container.CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DeleteIfExistsAsync();
        }

        _memoryCache.Dispose();
    }

    public string NewBlobPath(string suffix)
    {
        return $"ttd/app/{Guid.NewGuid()}/{suffix}";
    }

    public async Task UploadText(string blobStoragePath, string content)
    {
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
        await Repository.WriteBlob(Org, stream, blobStoragePath, null);
    }

    public async Task<BlobLeaseClient> AcquireLease(string blobStoragePath)
    {
        BlobLeaseClient leaseClient = _container
            .GetBlobClient(blobStoragePath)
            .GetBlobLeaseClient();
        await leaseClient.AcquireAsync(TimeSpan.FromSeconds(60));

        return leaseClient;
    }

    public async Task<bool> Exists(string blobStoragePath)
    {
        return await _container.GetBlobClient(blobStoragePath).ExistsAsync();
    }
}
