using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Azure.Storage;
using Azure.Storage.Blobs;
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

        string result = BlobRepository.GetVersionedBlobPath(
            "ttd/app",
            "instance-guid",
            blobVersionId
        );

        Assert.Equal($"ttd/app/instance-guid/data-elements/{blobVersionId}", result);
    }
}

public class BlobRepositoryAzuriteTests : IClassFixture<BlobRepositoryAzuriteFixture>
{
    private readonly BlobRepositoryAzuriteFixture _fixture;

    public BlobRepositoryAzuriteTests(BlobRepositoryAzuriteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WriteBlob_ThenReadBlob_RoundtripsContent()
    {
        string expectedContent = $"content-{Guid.NewGuid():N}";
        string blobStoragePath = _fixture.NewBlobPath("data-elements/version-1");

        await using MemoryStream upload = new(Encoding.UTF8.GetBytes(expectedContent));
        (long contentLength, DateTimeOffset lastModified) = await _fixture.Repository.WriteBlob(
            BlobRepositoryAzuriteFixture.Org,
            upload,
            blobStoragePath,
            null
        );

        using Stream downloaded = await _fixture.Repository.ReadBlob(
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
        Stream result = await _fixture.Repository.ReadBlob(
            BlobRepositoryAzuriteFixture.Org,
            _fixture.NewBlobPath("missing"),
            null
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteBlob_ExistingThenMissing_IsIdempotent()
    {
        string blobStoragePath = _fixture.NewBlobPath("data-elements/version-delete");
        await _fixture.UploadText(blobStoragePath, "delete me");

        bool firstDelete = await _fixture.Repository.DeleteBlob(
            BlobRepositoryAzuriteFixture.Org,
            blobStoragePath,
            null
        );
        bool secondDelete = await _fixture.Repository.DeleteBlob(
            BlobRepositoryAzuriteFixture.Org,
            blobStoragePath,
            null
        );

        Assert.True(firstDelete);
        Assert.False(secondDelete);
        Assert.False(await _fixture.Exists(blobStoragePath));
    }

    [Fact]
    public async Task DeleteDataBlobs_DeletesTargetInstancePrefixOnly()
    {
        string targetInstanceGuid = Guid.NewGuid().ToString();
        string otherInstanceGuid = Guid.NewGuid().ToString();
        string firstTargetBlob = $"ttd/app/{targetInstanceGuid}/data-elements/version-1";
        string secondTargetBlob = $"ttd/app/{targetInstanceGuid}/data/legacy";
        string otherInstanceBlob = $"ttd/app/{otherInstanceGuid}/data-elements/version-2";

        await _fixture.UploadText(firstTargetBlob, "first target");
        await _fixture.UploadText(secondTargetBlob, "second target");
        await _fixture.UploadText(otherInstanceBlob, "other instance");

        bool result = await _fixture.Repository.DeleteDataBlobs(
            BlobRepositoryAzuriteFixture.Org,
            "ttd/app",
            targetInstanceGuid,
            null
        );

        Assert.True(result);
        Assert.False(await _fixture.Exists(firstTargetBlob));
        Assert.False(await _fixture.Exists(secondTargetBlob));
        Assert.True(await _fixture.Exists(otherInstanceBlob));
    }

    [Fact]
    public async Task DeleteBlobs_DeletesDistinctExistingPaths()
    {
        string firstBlob = _fixture.NewBlobPath("data-elements/batch-1");
        string secondBlob = _fixture.NewBlobPath("data-elements/batch-2");
        await _fixture.UploadText(firstBlob, "first batch");
        await _fixture.UploadText(secondBlob, "second batch");

        bool result = await _fixture.Repository.DeleteBlobs(
            BlobRepositoryAzuriteFixture.Org,
            [firstBlob, secondBlob, firstBlob],
            null
        );

        Assert.True(result);
        Assert.False(await _fixture.Exists(firstBlob));
        Assert.False(await _fixture.Exists(secondBlob));
    }

    [Fact]
    public async Task DeleteBlobs_MissingPath_ReturnsFalse()
    {
        bool result = await _fixture.Repository.DeleteBlobs(
            BlobRepositoryAzuriteFixture.Org,
            [_fixture.NewBlobPath("missing-batch")],
            null
        );

        Assert.False(result);
    }
}

public sealed class BlobRepositoryAzuriteFixture : IAsyncLifetime
{
    public const string Org = "ttd";

    private const string AccountName = "devstoreaccount1";
    private const string AccountKey =
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
            AccountName = AccountName,
            AccountKey = AccountKey,
            BlobEndPoint = _blobEndPoint,
            OrgStorageAccount = AccountName,
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
        StorageSharedKeyCredential storageCredentials = new(AccountName, AccountKey);
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

    public async Task<bool> Exists(string blobStoragePath)
    {
        return await _container.GetBlobClient(blobStoragePath).ExistsAsync();
    }
}
