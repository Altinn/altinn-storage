using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class BlobRepositoryAzuriteTests(BlobRepositoryAzuriteFixture fixture)
    : IClassFixture<BlobRepositoryAzuriteFixture>
{
    [Fact]
    public async Task FileSystemAccount_EveryOperation_ThrowsNotSupported()
    {
        StorageAccount account = new(7, new FileSystemProviderInfo("/data/blobs/ttd"));
        string blobStoragePath = fixture.NewBlobPath("file-system");
        const string expectedMessage =
            "Storage account provider is not supported: FileSystemProviderInfo";

        Func<Task>[] operations =
        [
            () => fixture.Repository.WriteBlob(account, new MemoryStream([1]), blobStoragePath),
            () => fixture.Repository.ReadBlob(account, blobStoragePath),
            () => fixture.Repository.DeleteBlob(account, blobStoragePath),
            () => fixture.Repository.DeleteBlobsIfExists(account, [blobStoragePath]),
            () => fixture.Repository.DeleteDataBlobs(account, "ttd/app", Guid.NewGuid()),
        ];

        foreach (Func<Task> operation in operations)
        {
            NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
                operation
            );
            Assert.Equal(expectedMessage, exception.Message);
        }
    }

    [Fact]
    public async Task WriteBlob_ThenReadBlob_RoundtripsContent()
    {
        string expectedContent = $"content-{Guid.NewGuid():N}";
        string blobStoragePath = fixture.NewBlobPath("data-elements/version-1");

        await using MemoryStream upload = new(Encoding.UTF8.GetBytes(expectedContent));
        (long contentLength, DateTimeOffset lastModified) = await fixture.Repository.WriteBlob(
            fixture.Account,
            upload,
            blobStoragePath
        );

        using Stream downloaded = await fixture.Repository.ReadBlob(
            fixture.Account,
            blobStoragePath
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
            fixture.Account,
            fixture.NewBlobPath("missing")
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteBlob_ExistingThenMissing_IsIdempotent()
    {
        string blobStoragePath = fixture.NewBlobPath("data-elements/version-delete");
        await fixture.UploadText(blobStoragePath, "delete me");

        bool firstDelete = await fixture.Repository.DeleteBlob(fixture.Account, blobStoragePath);
        bool secondDelete = await fixture.Repository.DeleteBlob(fixture.Account, blobStoragePath);

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
            fixture.Account,
            "ttd/app",
            new Guid(targetInstanceGuid)
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
            fixture.Account,
            [existingBlob, missingBlob, existingBlob]
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
                fixture.Account,
                [existingBlob, leasedBlob, missingBlob]
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

    [Fact]
    public async Task GetReadUrl_ExistingBlob_ReadsContentAndLastModified()
    {
        string expectedContent = $"content-{Guid.NewGuid():N}";
        string blobStoragePath = fixture.NewBlobPath("data-elements/read-url");
        await using MemoryStream upload = new(Encoding.UTF8.GetBytes(expectedContent));
        (_, DateTimeOffset lastModified) = await fixture.Repository.WriteBlob(
            fixture.Account,
            upload,
            blobStoragePath
        );

        (Uri url, DateTimeOffset expiresOn) = await fixture.Repository.GetReadUrl(
            fixture.Account,
            blobStoragePath,
            TimeSpan.FromMinutes(5)
        );
        using HttpClient client = TestContainers.CreateAzuriteHttpClient();
        using HttpResponseMessage head = await client.SendAsync(new(HttpMethod.Head, url));
        string content = await client.GetStringAsync(url);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(lastModified, head.Content.Headers.LastModified);
        Assert.Equal(expectedContent, content);
        Assert.InRange(
            expiresOn,
            DateTimeOffset.UtcNow.AddMinutes(4),
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
    }

    [Fact]
    public async Task GetReadUrl_GrantsOnlyReadingThatBlob()
    {
        string blobStoragePath = fixture.NewBlobPath("data-elements/read-only");
        string otherBlobStoragePath = fixture.NewBlobPath("data-elements/other");
        await fixture.UploadText(blobStoragePath, "original");
        await fixture.UploadText(otherBlobStoragePath, "other");
        (Uri url, _) = await fixture.Repository.GetReadUrl(
            fixture.Account,
            blobStoragePath,
            TimeSpan.FromMinutes(5)
        );
        Uri otherBlobUrl = new(
            url.AbsoluteUri.Replace(blobStoragePath, otherBlobStoragePath, StringComparison.Ordinal)
        );
        using HttpClient client = TestContainers.CreateAzuriteHttpClient();
        using HttpRequestMessage put = new(HttpMethod.Put, url)
        {
            Content = new StringContent("replaced"),
        };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");

        using HttpResponseMessage putResponse = await client.SendAsync(put);
        using HttpResponseMessage deleteResponse = await client.DeleteAsync(url);
        using HttpResponseMessage otherBlobResponse = await client.GetAsync(otherBlobUrl);

        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherBlobResponse.StatusCode);
        Assert.Equal("original", await client.GetStringAsync(url));
    }

    [Fact]
    public async Task GetReadUrl_AfterExpiry_IsRefused()
    {
        string blobStoragePath = fixture.NewBlobPath("data-elements/expired");
        await fixture.UploadText(blobStoragePath, "expires");
        (Uri url, _) = await fixture.Repository.GetReadUrl(
            fixture.Account,
            blobStoragePath,
            TimeSpan.FromSeconds(1)
        );

        await Task.Delay(TimeSpan.FromSeconds(2));
        using HttpClient client = TestContainers.CreateAzuriteHttpClient();
        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReadUrl_IsUserDelegationSasForHttpsOnly()
    {
        string blobStoragePath = fixture.NewBlobPath("data-elements/delegated");
        await fixture.UploadText(blobStoragePath, "delegated");

        (Uri url, _) = await fixture.Repository.GetReadUrl(
            fixture.Account,
            blobStoragePath,
            TimeSpan.FromMinutes(5)
        );
        BlobSasQueryParameters sas = new BlobUriBuilder(url).Sas;

        Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        Assert.Equal(Guid.Empty.ToString(), sas.KeyObjectId);
        Assert.Equal("r", sas.Permissions);
        Assert.Equal(SasProtocol.Https, sas.Protocol);
        Assert.Equal("b", sas.Resource);
    }

    [Fact]
    public async Task GetReadUrl_ReusesUserDelegationKey()
    {
        string firstBlobStoragePath = fixture.NewBlobPath("data-elements/first");
        string secondBlobStoragePath = fixture.NewBlobPath("data-elements/second");
        await fixture.UploadText(firstBlobStoragePath, "first");
        await fixture.UploadText(secondBlobStoragePath, "second");
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        AzuriteTokenCredential credential = new();
        BlobRepository repository = fixture.CreateRepository(memoryCache, credential);

        (Uri firstUrl, _) = await repository.GetReadUrl(
            fixture.Account,
            firstBlobStoragePath,
            TimeSpan.FromMinutes(5)
        );
        (Uri secondUrl, _) = await repository.GetReadUrl(
            fixture.Account,
            secondBlobStoragePath,
            TimeSpan.FromHours(1)
        );
        using HttpClient client = TestContainers.CreateAzuriteHttpClient();

        Assert.Equal(1, credential.RequestCount);
        Assert.Equal("first", await client.GetStringAsync(firstUrl));
        Assert.Equal("second", await client.GetStringAsync(secondUrl));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public async Task GetReadUrl_LifetimeOutOfRange_Throws(int seconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Repository.GetReadUrl(
                fixture.Account,
                fixture.NewBlobPath("lifetime"),
                TimeSpan.FromSeconds(seconds)
            )
        );
    }
}

public class BlobRepositoryEndpointTests
{
    private const string _configured = "http://127.0.0.1:10000/devstoreaccount1";

    [Theory]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1", true)]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/", true)]
    [InlineData("HTTP://127.0.0.1:10000/devstoreaccount1", true)]
    [InlineData("http://127.0.0.1:10000/otheraccount", false)]
    [InlineData("http://127.0.0.1:10000/", false)]
    [InlineData("https://127.0.0.1:10000/devstoreaccount1", false)]
    [InlineData("http://127.0.0.1:10001/devstoreaccount1", false)]
    [InlineData("http://localhost:10000/devstoreaccount1", false)]
    public void IsConfiguredEndpoint_ComparesSchemeHostPortAndPath(string endpoint, bool expected)
    {
        Assert.Equal(expected, BlobRepository.IsConfiguredEndpoint(new Uri(endpoint), _configured));
    }

    [Fact]
    public void IsConfiguredEndpoint_ConfiguredEndpointWithTrailingSlash_Matches()
    {
        Assert.True(
            BlobRepository.IsConfiguredEndpoint(
                new Uri("http://127.0.0.1:10000/devstoreaccount1"),
                _configured + "/"
            )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    public void IsConfiguredEndpoint_NoUsableConfiguredEndpoint_ReturnsFalse(string? configured)
    {
        Assert.False(BlobRepository.IsConfiguredEndpoint(new Uri(_configured), configured));
    }

    [Fact]
    public void IsConfiguredEndpoint_NoEndpoint_ReturnsFalse()
    {
        Assert.False(BlobRepository.IsConfiguredEndpoint(null, _configured));
    }
}

public sealed class BlobRepositoryAzuriteFixture : IAsyncLifetime
{
    public const string Org = "ttd";

    private const string _accountName = TestContainers.AzuriteAccountName;
    private const string _accountKey = TestContainers.AzuriteAccountKey;
    private static readonly string _blobEndPoint = TestContainers.AzuriteBlobEndpoint;
    private readonly string _containerName = $"blobrepo-{Guid.NewGuid():N}"[..21];
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private BlobContainerClient _container = null!;

    public BlobRepositoryAzuriteFixture()
    {
        Configuration = new()
        {
            AccountName = _accountName,
            AccountKey = _accountKey,
            BlobEndPoint = _blobEndPoint,
            OrgStorageAccount = _accountName,
            OrgStorageContainer = $"{_containerName}-{{0}}",
        };

        Repository = CreateRepository(_memoryCache, new AzuriteTokenCredential());

        ContainerName = string.Format(Configuration.OrgStorageContainer, Org);
        Account = new StorageAccount(null, ProviderInfo(ContainerName));
    }

    /// <summary>
    /// Gets the configuration of <see cref="Repository"/>, whose naming pattern gives a container per org
    /// in this fixture's Azurite account.
    /// </summary>
    public AzureStorageConfiguration Configuration { get; }

    public BlobRepository Repository { get; }

    public StorageAccount Account { get; }

    public string ContainerName { get; }

    public async Task InitializeAsync()
    {
        _container = await CreateContainer(ContainerName);
    }

    /// <summary>
    /// Creates a container in the Azurite account. The caller deletes it.
    /// </summary>
    public static async Task<BlobContainerClient> CreateContainer(string containerName)
    {
        StorageSharedKeyCredential storageCredentials = new(_accountName, _accountKey);
        BlobServiceClient blobServiceClient = new(
            new Uri(_blobEndPoint),
            storageCredentials,
            TestContainers.AzuriteClientOptions
        );

        BlobContainerClient container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync();
        return container;
    }

    /// <summary>
    /// Gets the provider info of a container in the Azurite account.
    /// </summary>
    public static AzureBlobProviderInfo ProviderInfo(string containerName)
    {
        return new AzureBlobProviderInfo(_accountName, containerName, new Uri(_blobEndPoint));
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DeleteIfExistsAsync();
        }

        _memoryCache.Dispose();
    }

    /// <summary>
    /// Creates a repository for the fixture's containers with its own cache and credential.
    /// </summary>
    public BlobRepository CreateRepository(IMemoryCache memoryCache, TokenCredential credential)
    {
        return new BlobRepository(
            memoryCache,
            Options.Create(Configuration),
            NullLogger<BlobRepository>.Instance,
            credential,
            TestContainers.AzuriteClientOptions
        );
    }

    public string NewBlobPath(string suffix)
    {
        return $"ttd/app/{Guid.NewGuid()}/{suffix}";
    }

    public async Task UploadText(string blobStoragePath, string content)
    {
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
        await Repository.WriteBlob(Account, stream, blobStoragePath);
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
