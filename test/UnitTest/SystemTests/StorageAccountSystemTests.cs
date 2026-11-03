using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.TestingRepositories;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.SystemTests;

/// <summary>
/// Runs the storage service in process with its real repositories against the unit test database and
/// Azurite, and checks through the HTTP API that blob versions follow the storage account rows.
/// </summary>
/// <remarks>
/// Storage account rows are never deleted, so every test uses its own org: its pattern container and its
/// rows route no other test's writes. Authorization is replaced by a PDP that permits every request.
/// </remarks>
[Collection("StoragePostgreSQL")]
public sealed class StorageAccountSystemTests : IAsyncLifetime
{
    private const int _partyId = 1337;
    private const string _app = "system-test";
    private const string _defaultDataType = "default";
    private const string _scannedDataType = "scanned";
    private const string _instancesPath = "storage/api/v1/instances";
    private const string _cleanupPath = "storage/api/v1/cleanup";

    /// <summary>
    /// When set, the file-scan test writes the message it read from the queue to this path, for
    /// checking it against the scanner's own model outside the test.
    /// </summary>
    private const string _fileScanMessageOutVariable = "STORAGE_FILESCAN_MESSAGE_OUT";

    private static readonly AzureStorageConfiguration _storageConfiguration =
        TestStorageAccounts.AzureConfiguration;

    private readonly string _org = $"sys{Guid.NewGuid():N}"[..12];
    private readonly NpgsqlDataSource _dataSource = ServiceUtil.GetSharedDataSource();
    private readonly List<BlobContainerClient> _containers = [];
    private readonly List<StorageHost> _hosts = [];
    private BlobContainerClient _patternContainer = null!;

    private string AppId => $"{_org}/{_app}";

    private Uri PatternContainerUri =>
        ((AzureBlobProviderInfo)TestStorageAccounts.PatternDefault(_org).ProviderInfo).ContainerUri;

    public async Task InitializeAsync()
    {
        _patternContainer = await CreateContainer(
            string.Format(_storageConfiguration.OrgStorageContainer, _org)
        );
    }

    public async Task DisposeAsync()
    {
        await PostgresUtil.UnfreezeTime();
        foreach (StorageHost host in _hosts)
        {
            await host.DisposeAsync();
        }

        foreach (BlobContainerClient container in _containers)
        {
            await container.DeleteIfExistsAsync();
        }

        await PostgresUtil.RunSql($"delete from storage.applications where org = '{_org}'");
    }

    [Fact]
    public async Task ExistingInstances_WritableRowInserted_NewVersionsInItsContainerAndCleanupDeletesEachFromItsOwn()
    {
        // Arrange
        StorageHost host = await StartHost(seedApplication: true);
        BlobContainerClient named = await CreateContainer($"named-{Guid.NewGuid():N}");
        Uri namedUri = BlobRepositoryAzuriteFixture.ProviderInfo(named.Name).ContainerUri;

        Guid first = await CreateInstance(host);
        Guid second = await CreateInstance(host);
        Guid firstOverwritten = await Upload(host, first, "first v1");
        Guid firstUntouched = await Upload(host, first, "first untouched");
        Guid secondOverwritten = await Upload(host, second, "second v1");
        Guid secondUntouched = await Upload(host, second, "second untouched");

        foreach (
            Guid element in new[]
            {
                firstOverwritten,
                firstUntouched,
                secondOverwritten,
                secondUntouched,
            }
        )
        {
            Assert.Null(Assert.Single(await ReadVersionRows(element)).StorageAccountId);
        }

        Assert.Equal(4, (await BlobNames(_patternContainer)).Count);
        Assert.Empty(await BlobNames(named));

        // Act
        short accountId = await InsertWritableAccount(host, named.Name);
        await Overwrite(host, first, firstOverwritten, "first v2");
        await Overwrite(host, first, firstOverwritten, "first v3");
        await Overwrite(host, second, secondOverwritten, "second v2");
        Guid firstAdded = await Upload(host, first, "first added");
        Guid secondAdded = await Upload(host, second, "second added");

        // Assert: rows
        List<VersionRow> firstRows = await ReadVersionRows(firstOverwritten);
        Assert.Equal(
            new short?[] { null, accountId, accountId },
            firstRows.Select(r => r.StorageAccountId)
        );
        Assert.Equal(new[] { true, true, false }, firstRows.Select(r => r.Detached));
        List<VersionRow> secondRows = await ReadVersionRows(secondOverwritten);
        Assert.Equal(new short?[] { null, accountId }, secondRows.Select(r => r.StorageAccountId));
        Assert.Equal(new[] { true, false }, secondRows.Select(r => r.Detached));
        Assert.Equal(accountId, Assert.Single(await ReadVersionRows(firstAdded)).StorageAccountId);
        Assert.Equal(accountId, Assert.Single(await ReadVersionRows(secondAdded)).StorageAccountId);
        Assert.Null(Assert.Single(await ReadVersionRows(firstUntouched)).StorageAccountId);
        Assert.Null(Assert.Single(await ReadVersionRows(secondUntouched)).StorageAccountId);

        // Assert: blobs
        Assert.Equal(
            new[]
            {
                BlobPath(first, firstRows[0].VersionId),
                BlobPath(first, (await ReadVersionRows(firstUntouched))[0].VersionId),
                BlobPath(second, secondRows[0].VersionId),
                BlobPath(second, (await ReadVersionRows(secondUntouched))[0].VersionId),
            }.Order(),
            (await BlobNames(_patternContainer)).Order()
        );
        Assert.Equal(
            new[]
            {
                BlobPath(first, firstRows[1].VersionId),
                BlobPath(first, firstRows[2].VersionId),
                BlobPath(second, secondRows[1].VersionId),
                BlobPath(first, (await ReadVersionRows(firstAdded))[0].VersionId),
                BlobPath(second, (await ReadVersionRows(secondAdded))[0].VersionId),
            }.Order(),
            (await BlobNames(named)).Order()
        );

        // Assert: downloads
        Assert.Equal("first v3", await ReadData(host, first, firstOverwritten));
        Assert.Equal("first untouched", await ReadData(host, first, firstUntouched));
        Assert.Equal("first added", await ReadData(host, first, firstAdded));
        Assert.Equal("second v2", await ReadData(host, second, secondOverwritten));
        Assert.Equal("second untouched", await ReadData(host, second, secondUntouched));
        Assert.Equal("second added", await ReadData(host, second, secondAdded));
        Assert.Equal(
            "first v1",
            await ReadVersion(host, first, firstOverwritten, firstRows[0].VersionId)
        );
        Assert.Equal(
            "first v2",
            await ReadVersion(host, first, firstOverwritten, firstRows[1].VersionId)
        );
        Assert.Equal(
            "second v1",
            await ReadVersion(host, second, secondOverwritten, secondRows[0].VersionId)
        );

        // Assert: audit before cleanup
        IReadOnlyList<BlobVersionAudit.Finding> before = await Audit(host, namedUri);
        Assert.Equal(9, before.Count);
        BlobVersionAudit.AssertConsistent(before);

        // Act and assert: detached cleanup when a data element is deleted
        using (
            HttpResponseMessage deleted = await host.Client.DeleteAsync(
                DataPath(second, secondOverwritten)
            )
        )
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        Assert.Empty(await ReadVersionRows(secondOverwritten));
        Assert.DoesNotContain(
            BlobPath(second, secondRows[0].VersionId),
            await BlobNames(_patternContainer)
        );
        Assert.DoesNotContain(BlobPath(second, secondRows[1].VersionId), await BlobNames(named));

        // Act and assert: orphan cleanup of versions detached more than seven days ago
        await PostgresUtil.FreezeTime(DateTimeOffset.UtcNow.AddDays(8));
        try
        {
            using HttpResponseMessage cleaned = await host.Client.DeleteAsync(
                $"{_cleanupPath}/cleanupdataelements"
            );
            Assert.Equal(HttpStatusCode.OK, cleaned.StatusCode);
        }
        finally
        {
            await PostgresUtil.UnfreezeTime();
        }

        Assert.Equal(
            new[] { firstRows[2].VersionId },
            (await ReadVersionRows(firstOverwritten)).Select(r => r.VersionId)
        );
        Assert.DoesNotContain(
            BlobPath(first, firstRows[0].VersionId),
            await BlobNames(_patternContainer)
        );
        Assert.DoesNotContain(BlobPath(first, firstRows[1].VersionId), await BlobNames(named));
        Assert.Contains(BlobPath(first, firstRows[2].VersionId), await BlobNames(named));
        Assert.Equal("first v3", await ReadData(host, first, firstOverwritten));

        // Act and assert: hard delete of the instances and instance cleanup
        foreach (Guid instance in new[] { first, second })
        {
            using HttpResponseMessage deleted = await host.Client.DeleteAsync(
                $"{InstancePath(instance)}?hard=true"
            );
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        await PostgresUtil.FreezeTime(DateTimeOffset.UtcNow.AddDays(8));
        try
        {
            using HttpResponseMessage cleaned = await host.Client.DeleteAsync(
                $"{_cleanupPath}/cleanupinstances"
            );
            Assert.Equal(HttpStatusCode.OK, cleaned.StatusCode);
        }
        finally
        {
            await PostgresUtil.UnfreezeTime();
        }

        Assert.Empty(await BlobNames(_patternContainer));
        Assert.Empty(await BlobNames(named));
        IReadOnlyList<BlobVersionAudit.Finding> after = await Audit(host, namedUri);
        BlobVersionAudit.AssertNoBlobs(after);
    }

    [Fact]
    public async Task WritableRowWithMissingContainer_UploadAndOverwriteFail_NoElementOrVersionRowIsLeft()
    {
        // Arrange
        StorageHost host = await StartHost(seedApplication: true);
        Guid instance = await CreateInstance(host);
        Guid existing = await Upload(host, instance, "kept");
        string missingContainer = $"missing-{Guid.NewGuid():N}";
        await InsertWritableAccount(host, missingContainer);

        // Act
        using HttpResponseMessage upload = await PostData(host, instance, _defaultDataType, "lost");
        using HttpResponseMessage overwrite = await PutData(host, instance, existing, "lost");

        // Assert
        // The test host runs in the Development environment, whose exception page returns the exception text.
        Assert.Equal(HttpStatusCode.InternalServerError, upload.StatusCode);
        Assert.Contains("ContainerNotFound", await upload.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, overwrite.StatusCode);
        Assert.Contains("ContainerNotFound", await overwrite.Content.ReadAsStringAsync());
        Assert.Equal(
            1,
            await PostgresUtil.RunCountQuery(
                $"select count(*) from storage.dataelements where instanceguid = '{instance}'"
            )
        );
        Assert.Equal(
            1,
            await PostgresUtil.RunCountQuery(
                $"select count(*) from storage.dataelementblobversions where instanceguid = '{instance}'"
            )
        );
        Assert.Null(Assert.Single(await ReadVersionRows(existing)).StorageAccountId);
        Assert.Equal("kept", await ReadData(host, instance, existing));
        BlobContainerClient missing = new(
            BlobRepositoryAzuriteFixture.ProviderInfo(missingContainer).ContainerUri,
            SharedKey()
        );
        Assert.False(await missing.ExistsAsync());
    }

    [Fact]
    public async Task StartHost_RowWithUnknownProvider_FailsToStart()
    {
        // Arrange
        short id = await PostgresUtil.RunQuery<short>(
            $$"""select storage.insertstorageaccount('unknown-provider', '{"accountName":"a","container":"c"}'::jsonb, '{{_org}}', 1, false)"""
        );

        try
        {
            await using TestApplicationFactory<DataController> root = new();
            WebApplicationFactory<DataController> factory = Configure(root, Settings(60, null));

            // Act
            Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            // Assert
            Assert.Contains($"Storage account {id} ", exception.ToString());
            Assert.Contains("'unknown-provider'", exception.ToString());
        }
        finally
        {
            // The row would stop every later host from starting.
            await PostgresUtil.RunSql($"delete from storage.storageaccounts where id = {id}");
        }
    }

    [Fact]
    public async Task TwoHosts_RowInsertedThroughOne_OtherRoutesToItAfterCacheExpires()
    {
        // Arrange
        const int cacheSeconds = 2;
        StorageHost hostA = await StartHost(cacheSeconds, seedApplication: true);
        StorageHost hostB = await StartHost(cacheSeconds);
        BlobContainerClient named = await CreateContainer($"named-{Guid.NewGuid():N}");
        Guid instance = await CreateInstance(hostA);

        // Fills host B's cache before the row exists.
        Assert.Null((await hostB.Resolver.ForWrite(_org, 1)).Id);
        Stopwatch sinceHostBCached = Stopwatch.StartNew();

        // Act
        short accountId = await InsertWritableAccount(hostA, named.Name);
        StorageAccount staleOnB = await hostB.Resolver.ForWrite(_org, 1);
        TimeSpan staleCheckedAfter = sinceHostBCached.Elapsed;
        Guid throughA = await Upload(hostA, instance, "through A");

        await Task.Delay(TimeSpan.FromSeconds(cacheSeconds) + TimeSpan.FromMilliseconds(500));
        Guid throughB = await Upload(hostB, instance, "through B");

        // Assert
        // Host B may only still route to the pattern default while its snapshot is younger than the TTL;
        // on a machine slow enough to pass the TTL before the check, the check proves nothing.
        if (staleCheckedAfter < TimeSpan.FromSeconds(cacheSeconds))
        {
            Assert.Null(staleOnB.Id);
        }

        Assert.Equal(accountId, Assert.Single(await ReadVersionRows(throughA)).StorageAccountId);
        Assert.Equal(accountId, Assert.Single(await ReadVersionRows(throughB)).StorageAccountId);
        Assert.Empty(await BlobNames(_patternContainer));
        Assert.Equal(2, (await BlobNames(named)).Count);
        Assert.Equal("through A", await ReadData(hostB, instance, throughA));
        Assert.Equal("through B", await ReadData(hostA, instance, throughB));
    }

    [Fact]
    public async Task ParallelUploads_RowInsertedMidway_EveryRowNamesTheAccountHoldingItsBlob()
    {
        // Arrange
        const int uploadsPerPhase = 10;
        StorageHost host = await StartHost(seedApplication: true);
        BlobContainerClient named = await CreateContainer($"named-{Guid.NewGuid():N}");
        Uri namedUri = BlobRepositoryAzuriteFixture.ProviderInfo(named.Name).ContainerUri;
        Guid instance = await CreateInstance(host);

        // Act
        List<Task<HttpResponseMessage>> uploads =
        [
            .. Enumerable
                .Range(0, uploadsPerPhase)
                .Select(i => PostData(host, instance, _defaultDataType, $"upload {i}")),
        ];
        await Task.WhenAny(uploads);
        short accountId = await InsertWritableAccount(host, named.Name);
        uploads.AddRange(
            Enumerable
                .Range(uploadsPerPhase, uploadsPerPhase)
                .Select(i => PostData(host, instance, _defaultDataType, $"upload {i}"))
        );
        HttpResponseMessage[] responses = await Task.WhenAll(uploads);

        // Assert
        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)
        );
        IReadOnlyList<BlobVersionAudit.Finding> findings = await Audit(host, namedUri);
        Assert.Equal(2 * uploadsPerPhase, findings.Count);
        BlobVersionAudit.AssertConsistent(findings);
        Assert.True(findings.Count(f => f.Row.StorageAccountId == accountId) >= uploadsPerPhase);
        Assert.All(
            findings,
            f => Assert.True(f.Row.StorageAccountId is null || f.Row.StorageAccountId == accountId)
        );

        foreach (HttpResponseMessage response in responses)
        {
            Guid element = Guid.Parse(
                JsonNode.Parse(await response.Content.ReadAsStringAsync())![
                    "id"
                ]!.GetValue<string>()
            );
            Assert.StartsWith("upload ", await ReadData(host, instance, element));
            response.Dispose();
        }
    }

    [Fact]
    public async Task UploadWithFileScan_WritableRow_QueueMessageNamesTheAccount()
    {
        // Arrange
        string queueName = $"file-scan-inbound-{Guid.NewGuid():N}";
        StorageHost host = await StartHost(seedApplication: true, fileScanQueueName: queueName);
        QueueStorageSettings queueSettings = host
            .Services.GetRequiredService<IOptions<QueueStorageSettings>>()
            .Value;
        QueueClient queue = new(queueSettings.ConnectionString, queueName);
        BlobContainerClient named = await CreateContainer($"named-{Guid.NewGuid():N}");
        Guid instance = await CreateInstance(host);

        try
        {
            // Act
            Guid patternElement = await Upload(host, instance, "pattern", _scannedDataType);
            short accountId = await InsertWritableAccount(host, named.Name);
            Guid namedElement = await Upload(host, instance, "named", _scannedDataType);
            QueueMessage[] messages = (await queue.ReceiveMessagesAsync(maxMessages: 10)).Value;

            // Assert
            Assert.Equal(2, messages.Length);
            Dictionary<JsonNode, string> messageTexts = messages
                .Select(message =>
                    Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.ToString()))
                )
                .ToDictionary(text => JsonNode.Parse(text)!);
            JsonNode[] requests = [.. messageTexts.Keys];
            JsonNode patternRequest = Assert.Single(
                requests,
                r => r["dataElementId"]!.GetValue<string>() == patternElement.ToString()
            );
            JsonNode namedRequest = Assert.Single(
                requests,
                r => r["dataElementId"]!.GetValue<string>() == namedElement.ToString()
            );

            Assert.Equal(_org, patternRequest["org"]!.GetValue<string>());
            Assert.Null(patternRequest["storageAccountId"]);
            Assert.Equal($"{_partyId}/{instance}", namedRequest["instanceId"]!.GetValue<string>());
            Assert.Equal(_org, namedRequest["org"]!.GetValue<string>());
            Assert.Equal(accountId, namedRequest["storageAccountId"]!.GetValue<short>());
            Assert.Equal(
                StorageAccountProviderInfo.AzureBlob,
                namedRequest["storageAccountProvider"]!.GetValue<string>()
            );
            Assert.Equal(
                named.Name,
                namedRequest["storageAccountProviderInfo"]!["container"]!.GetValue<string>()
            );
            VersionRow namedRow = Assert.Single(await ReadVersionRows(namedElement));
            Assert.Equal(namedRow.VersionId, namedRequest["blobVersionId"]!.GetValue<string>());
            Assert.Equal(
                BlobPath(instance, namedRow.VersionId),
                namedRequest["blobStoragePath"]!.GetValue<string>()
            );

            string? messageOut = Environment.GetEnvironmentVariable(_fileScanMessageOutVariable);
            if (!string.IsNullOrEmpty(messageOut))
            {
                await File.WriteAllTextAsync(messageOut, messageTexts[namedRequest]);
            }
        }
        finally
        {
            await queue.DeleteIfExistsAsync();
        }
    }

    private static Dictionary<string, string?> Settings(int cacheSeconds, string? fileScanQueueName)
    {
        Dictionary<string, string?> settings = new()
        {
            ["StorageAccounts:CacheSeconds"] = cacheSeconds.ToString(),
            ["GeneralSettings:DisableA2Endpoints"] = "true",
        };
        if (fileScanQueueName is not null)
        {
            settings["QueueStorageSettings:FileScanQueueName"] = fileScanQueueName;
        }

        return settings;
    }

    private static WebApplicationFactory<DataController> Configure(
        TestApplicationFactory<DataController> root,
        Dictionary<string, string?> settings
    ) =>
        root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, config) => config.AddInMemoryCollection(settings)
            );
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<
                    IPostConfigureOptions<JwtCookieOptions>,
                    JwtCookiePostConfigureOptionsStub
                >();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                services.AddSingleton(Mock.Of<IKeyVaultClientWrapper>());
                services.AddSingleton(Mock.Of<IPartiesWithInstancesClient>());
                services.AddSingleton(Mock.Of<IMessageBus>());
                services.AddSingleton<IPDP, PermitAllPdp>();
            });
        });

    private async Task<StorageHost> StartHost(
        int cacheSeconds = 60,
        bool seedApplication = false,
        string? fileScanQueueName = null
    )
    {
        TestApplicationFactory<DataController> root = new();
        WebApplicationFactory<DataController> factory = Configure(
            root,
            Settings(cacheSeconds, fileScanQueueName)
        );
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrincipalUtil.GetOrgToken(_org));
        StorageHost host = new(root, factory, client);
        _hosts.Add(host);

        if (seedApplication)
        {
            await host
                .Services.GetRequiredService<IApplicationRepository>()
                .Create(
                    new Application
                    {
                        Id = AppId,
                        Org = _org,
                        Title = new Dictionary<string, string> { ["nb"] = "System test" },
                        DataTypes =
                        [
                            new DataType { Id = _defaultDataType },
                            new DataType { Id = _scannedDataType, EnableFileScan = true },
                        ],
                    }
                );
        }

        return host;
    }

    private async Task<BlobContainerClient> CreateContainer(string name)
    {
        BlobContainerClient container = await BlobRepositoryAzuriteFixture.CreateContainer(name);
        _containers.Add(container);
        return container;
    }

    private async Task<short> InsertWritableAccount(StorageHost host, string containerName)
    {
        short id = await host
            .Services.GetRequiredService<IStorageAccountRepository>()
            .Insert(
                BlobRepositoryAzuriteFixture.ProviderInfo(containerName),
                _org,
                StorageAccountNaming.DefaultStorageAccountNumber,
                writable: true
            );
        host.ClearCache();
        return id;
    }

    private Task<IReadOnlyList<BlobVersionAudit.Finding>> Audit(
        StorageHost host,
        Uri namedContainer
    ) =>
        BlobVersionAudit.Run(
            _dataSource,
            host.Resolver,
            _storageConfiguration,
            _org,
            [PatternContainerUri, namedContainer]
        );

    private async Task<Guid> CreateInstance(StorageHost host)
    {
        using StringContent body = new(
            $$$"""{"instanceOwner":{"partyId":"{{{_partyId}}}"}}""",
            Encoding.UTF8,
            "application/json"
        );
        using HttpResponseMessage response = await host.Client.PostAsync(
            $"{_instancesPath}?appId={AppId}",
            body
        );
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        return Guid.Parse(JsonNode.Parse(content)!["id"]!.GetValue<string>().Split('/')[1]);
    }

    private async Task<Guid> Upload(
        StorageHost host,
        Guid instance,
        string content,
        string dataType = _defaultDataType
    )
    {
        using HttpResponseMessage response = await PostData(host, instance, dataType, content);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return Guid.Parse(JsonNode.Parse(body)!["id"]!.GetValue<string>());
    }

    private static async Task<HttpResponseMessage> PostData(
        StorageHost host,
        Guid instance,
        string dataType,
        string content
    )
    {
        using StringContent body = new(content, Encoding.UTF8, "text/plain");
        return await host.Client.PostAsync(
            $"{InstancePath(instance)}/data?dataType={dataType}",
            body
        );
    }

    private static async Task<HttpResponseMessage> PutData(
        StorageHost host,
        Guid instance,
        Guid element,
        string content
    )
    {
        using StringContent body = new(content, Encoding.UTF8, "text/plain");
        return await host.Client.PutAsync(DataPath(instance, element), body);
    }

    private static async Task Overwrite(
        StorageHost host,
        Guid instance,
        Guid element,
        string content
    )
    {
        using HttpResponseMessage response = await PutData(host, instance, element, content);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync()
        );
    }

    private static async Task<string> ReadData(StorageHost host, Guid instance, Guid element)
    {
        using HttpResponseMessage response = await host.Client.GetAsync(
            DataPath(instance, element)
        );
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return content;
    }

    private static async Task<string> ReadVersion(
        StorageHost host,
        Guid instance,
        Guid element,
        string versionId
    )
    {
        using HttpResponseMessage response = await host.Client.GetAsync(
            $"{DataPath(instance, element)}/versions/{versionId}"
        );
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return content;
    }

    private static string InstancePath(Guid instance) => $"{_instancesPath}/{_partyId}/{instance}";

    private static string DataPath(Guid instance, Guid element) =>
        $"{InstancePath(instance)}/data/{element}";

    private string BlobPath(Guid instance, string versionId) =>
        DataElementHelper.GetVersionedBlobPath(AppId, instance, versionId);

    private async Task<List<VersionRow>> ReadVersionRows(Guid dataElementId)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand(
            "select id, storageaccountid, detachedat is not null from storage.dataelementblobversions where dataelementid = $1 order by created, id"
        );
        command.Parameters.AddWithValue(dataElementId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<VersionRow> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add(
                new VersionRow(
                    BlobVersionId.Encode(reader.GetGuid(0)),
                    reader.IsDBNull(1) ? null : reader.GetInt16(1),
                    reader.GetBoolean(2)
                )
            );
        }

        return rows;
    }

    private static async Task<List<string>> BlobNames(BlobContainerClient container)
    {
        List<string> names = [];
        await foreach (BlobItem blob in container.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    private static Azure.Storage.StorageSharedKeyCredential SharedKey() =>
        new(_storageConfiguration.AccountName, _storageConfiguration.AccountKey);

    private sealed record VersionRow(string VersionId, short? StorageAccountId, bool Detached);

    /// <summary>
    /// A running storage service with its own memory cache, so its storage account resolver keeps its
    /// own snapshot of the rows like a pod does.
    /// </summary>
    private sealed class StorageHost(
        TestApplicationFactory<DataController> root,
        WebApplicationFactory<DataController> factory,
        HttpClient client
    ) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public IServiceProvider Services => factory.Services;

        public IStorageAccountResolver Resolver =>
            Services.GetRequiredService<IStorageAccountResolver>();

        /// <summary>
        /// Empties the host's memory cache, as the cache expiring does.
        /// </summary>
        public void ClearCache() =>
            ((MemoryCache)Services.GetRequiredService<IMemoryCache>()).Clear();

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await root.DisposeAsync();
        }
    }

    /// <summary>
    /// Permits every request, one result per request reference.
    /// </summary>
    private sealed class PermitAllPdp : IPDP
    {
        public Task<XacmlJsonResponse> GetDecisionForRequest(
            XacmlJsonRequestRoot xacmlJsonRequest
        ) => Task.FromResult(Permit(xacmlJsonRequest));

        public Task<XacmlJsonResponse> GetDecisionForRequest(
            XacmlJsonRequestRoot xacmlJsonRequest,
            CancellationToken cancellationToken
        ) => Task.FromResult(Permit(xacmlJsonRequest));

        public Task<bool> GetDecisionForUnvalidateRequest(
            XacmlJsonRequestRoot xacmlJsonRequest,
            ClaimsPrincipal user
        ) => Task.FromResult(true);

        public Task<bool> GetDecisionForUnvalidateRequest(
            XacmlJsonRequestRoot xacmlJsonRequest,
            ClaimsPrincipal user,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);

        private static XacmlJsonResponse Permit(XacmlJsonRequestRoot request)
        {
            int count = Math.Max(1, request.Request.MultiRequests?.RequestReference?.Count ?? 1);
            return new XacmlJsonResponse
            {
                Response =
                [
                    .. Enumerable
                        .Range(0, count)
                        .Select(_ => new XacmlJsonResult { Decision = "Permit" }),
                ],
            };
        }
    }
}
