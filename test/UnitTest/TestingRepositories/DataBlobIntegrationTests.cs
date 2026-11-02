#nullable disable

using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.Constants;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class DataBlobIntegrationTests
    : IClassFixture<DataElementFixture>,
        IClassFixture<BlobRepositoryAzuriteFixture>
{
    private readonly DataElementFixture _dataElementFixture;
    private readonly BlobRepositoryAzuriteFixture _blobFixture;
    private readonly InstanceInternal _instanceInternal;
    private readonly long _instanceInternalId;

    public DataBlobIntegrationTests(
        DataElementFixture dataElementFixture,
        BlobRepositoryAzuriteFixture blobFixture
    )
    {
        _dataElementFixture = dataElementFixture;
        _blobFixture = blobFixture;

        string sql =
            "delete from storage.instanceevents; delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Org = BlobRepositoryAzuriteFixture.Org;
        instance.AppId = $"{BlobRepositoryAzuriteFixture.Org}/test-applikasjon-1";
        InstanceInternal createdInstance = _dataElementFixture
            .InstanceRepo.Create(instance, CancellationToken.None)
            .Result;
        _instanceInternal = _dataElementFixture
            .InstanceRepo.GetOne(createdInstance.Id, false, CancellationToken.None)
            .Result;
        _instanceInternalId = _instanceInternal.InternalId;
    }

    [Fact]
    public async Task UploadAndDelete_WithPostgresAndAzurite_PersistsAndRemovesMetadataAndBlob()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new();
        DataService dataService = new(
            fileScanQueueClientMock.Object,
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            CreateResolver()
        );
        Guid dataElementId = Guid.NewGuid();
        string content = $"integration-content-{Guid.NewGuid():N}";
        DataElementCreateOptions options = new()
        {
            DataElementId = dataElementId,
            DataType = "default",
            ContentType = "text/plain",
            Filename = "integration.txt",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        // Act
        (DataElementInternal createdDataElement, DateTimeOffset blobTimestamp, _, _) =
            await dataService.UploadDataAndCreateDataElement(
                _instanceInternal,
                new MemoryStream(Encoding.UTF8.GetBytes(content)),
                options,
                _instanceInternalId,
                new Application(),
                cancellationToken: CancellationToken.None
            );

        // Assert upload
        Assert.NotEqual(default, blobTimestamp);
        Assert.False(string.IsNullOrEmpty(createdDataElement.BlobVersionId));
        Assert.EndsWith(
            $"/data-elements/{createdDataElement.BlobVersionId}",
            createdDataElement.BlobStoragePath,
            StringComparison.Ordinal
        );

        DataElementInternal readDataElement = await _dataElementFixture.DataRepo.Read(
            createdDataElement.InstanceGuid,
            dataElementId,
            CancellationToken.None
        );
        Assert.Equal(createdDataElement.BlobVersionId, readDataElement.BlobVersionId);

        using Stream readBlob = await _blobFixture.Repository.ReadBlob(
            _blobFixture.Account,
            createdDataElement.BlobStoragePath,
            CancellationToken.None
        );
        using StreamReader reader = new(readBlob, Encoding.UTF8);
        Assert.Equal(content, await reader.ReadToEndAsync());
        Assert.True(await _blobFixture.Exists(createdDataElement.BlobStoragePath));
        Assert.Single(
            await _dataElementFixture.DataRepo.ReadBlobVersions(
                createdDataElement.InstanceGuid,
                dataElementId
            )
        );

        // Act delete
        Guid instanceGuid = createdDataElement.InstanceGuid;
        InstanceMutationCommit mutation = new(
            [],
            [],
            [new InstanceMutationDataElementDelete(createdDataElement, IgnoreLock: false)],
            _instanceInternal,
            [],
            null,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = dataElementId.ToString(),
                    Created = DateTime.UtcNow,
                },
            ]
        );
        await _dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation,
            CancellationToken.None
        );
        await dataService.CleanupDeletedDataElementBlobs(
            _instanceInternal,
            createdDataElement,
            StorageAccountNaming.DefaultStorageAccountNumber,
            CancellationToken.None
        );

        // Assert delete
        Assert.Null(
            await _dataElementFixture.DataRepo.Read(
                createdDataElement.InstanceGuid,
                dataElementId,
                CancellationToken.None
            )
        );
        Assert.Empty(
            await _dataElementFixture.DataRepo.ReadBlobVersions(
                createdDataElement.InstanceGuid,
                dataElementId
            )
        );
        Assert.False(await _blobFixture.Exists(createdDataElement.BlobStoragePath));
        Assert.Equal(1, await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted));
    }

    [Fact]
    public async Task CommitAggregateDelete_RetainsVersionBytesForServiceOwnerAndRemovesLegacyBlob()
    {
        // Arrange
        IStorageAccountResolver resolver = CreateResolver();
        DataService dataService = new(
            Mock.Of<IFileScanQueueClient>(),
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            resolver
        );
        Guid dataElementId = Guid.NewGuid();
        byte[] originalContent = "original version content"u8.ToArray();
        using MemoryStream uploadStream = new(originalContent);
        DataUploadResult upload = await dataService.UploadDataAndCreateDataElement(
            _instanceInternal,
            uploadStream,
            new DataElementCreateOptions
            {
                DataElementId = dataElementId,
                DataType = "default",
                ContentType = "text/plain",
                Filename = "original.txt",
                Created = DateTime.UtcNow,
                CreatedBy = "ttd",
            },
            _instanceInternalId,
            new Application(),
            cancellationToken: CancellationToken.None
        );
        string originalVersionId = upload.DataElement.BlobVersionId;
        string legacyPath = DataElementHelper.DataFileName(
            _instanceInternal.AppId,
            _instanceInternal.Id,
            dataElementId
        );
        await _blobFixture.UploadText(legacyPath, "legacy content");
        Application application = new()
        {
            Id = _instanceInternal.AppId,
            Org = _instanceInternal.Org,
            DataTypes = [new DataType { Id = "default" }],
        };
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(repository =>
                repository.FindOne(application.Id, application.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(application);
        Mock<IAuthorization> authorization = new();
        authorization
            .Setup(service =>
                service.AuthorizeEnrichedInstanceAction(It.IsAny<InstanceInternal>(), "read")
            )
            .ReturnsAsync(true);
        Mock<IInstanceEventService> eventService = new();
        eventService
            .Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId)
                )
            )
            .Returns(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = dataElementId.ToString(),
                    Created = DateTime.UtcNow,
                }
            );
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(AltinnCoreClaimTypes.Org, _instanceInternal.Org),
                        new Claim(AltinnCoreClaimTypes.OrgNumber, "111111111"),
                    ],
                    "test"
                )
            ),
        };
        using MemoryStream requestBody = new(
            Encoding.UTF8.GetBytes(
                $$"""{"deleteDataElements":[{"dataElementId":"{{dataElementId}}"}]}"""
            )
        );
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = requestBody.Length;
        httpContext.Request.Body = requestBody;
        IOptions<GeneralSettings> settings = Options.Create(
            new GeneralSettings { Hostname = "https://altinn.no/" }
        );
        InstanceMutationsController mutationController = new(
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            _dataElementFixture.InstanceRepo,
            _dataElementFixture.InstanceMutationRepo,
            applicationRepository.Object,
            dataService,
            eventService.Object,
            settings,
            authorization.Object,
            Mock.Of<IAuthorizationService>(),
            Mock.Of<IProcessAuthorizer>(),
            resolver
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        DataController dataController = new(
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            _dataElementFixture.InstanceRepo,
            _dataElementFixture.InstanceMutationRepo,
            applicationRepository.Object,
            dataService,
            eventService.Object,
            settings,
            null,
            authorization.Object,
            resolver
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        int partyId = int.Parse(_instanceInternal.InstanceOwner.PartyId);

        // Act
        ActionResult<InstanceMutationResponse> deletion = await mutationController.CommitMutation(
            partyId,
            _instanceInternal.Id,
            CancellationToken.None
        );
        Assert.IsType<OkObjectResult>(deletion.Result);
        ActionResult read = await dataController.GetBlobVersion(
            partyId,
            _instanceInternal.Id,
            dataElementId,
            originalVersionId,
            CancellationToken.None
        );

        // Assert
        Assert.Null(await _dataElementFixture.DataRepo.Read(_instanceInternal.Id, dataElementId));
        Assert.False(await _blobFixture.Exists(legacyPath));
        FileStreamResult file = Assert.IsType<FileStreamResult>(read);
        using Stream retainedStream = file.FileStream;
        using MemoryStream retainedContent = new();
        await retainedStream.CopyToAsync(retainedContent);
        Assert.Equal(originalContent, retainedContent.ToArray());
    }

    [Fact]
    public async Task WritableAccountRow_RoutesNewUploadsAndReadsFollowEachVersion()
    {
        // Arrange
        string org = $"acc{Guid.NewGuid():N}"[..12];
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Id = Guid.NewGuid();
        instance.Org = org;
        instance.AppId = $"{org}/test-applikasjon-1";
        InstanceInternal created = await _dataElementFixture.InstanceRepo.Create(
            instance,
            CancellationToken.None
        );
        instance = await _dataElementFixture.InstanceRepo.GetOne(
            created.Id,
            false,
            CancellationToken.None
        );
        int partyId = int.Parse(instance.InstanceOwner.PartyId);
        string patternContainerName = string.Format(
            _blobFixture.Configuration.OrgStorageContainer,
            org
        );
        string namedContainerName = $"named-{Guid.NewGuid():N}";
        BlobContainerClient patternContainer = await BlobRepositoryAzuriteFixture.CreateContainer(
            patternContainerName
        );
        BlobContainerClient namedContainer = await BlobRepositoryAzuriteFixture.CreateContainer(
            namedContainerName
        );
        Application application = new()
        {
            Id = instance.AppId,
            Org = org,
            DataTypes = [new DataType { Id = "default" }],
        };
        PgStorageAccountRepository accountRepository = new(_dataElementFixture.DataSource);

        try
        {
            DataUploadResult before = await Upload(
                CreateResolver(),
                instance,
                application,
                "before the row"
            );

            short accountId = await accountRepository.Insert(
                BlobRepositoryAzuriteFixture.ProviderInfo(namedContainerName),
                org,
                StorageAccountNaming.DefaultStorageAccountNumber,
                writable: true
            );

            // A new resolver stands in for the cache expiring after the row was inserted.
            IStorageAccountResolver resolver = CreateResolver();

            // Act
            DataUploadResult after = await Upload(resolver, instance, application, "after the row");
            DataController dataController = CreateDataController(resolver, application, org);
            ActionResult readBefore = await dataController.Get(
                partyId,
                instance.Id,
                before.DataElement.Id,
                CancellationToken.None
            );
            ActionResult readAfter = await dataController.Get(
                partyId,
                instance.Id,
                after.DataElement.Id,
                CancellationToken.None
            );

            // Assert
            Assert.Null(before.StorageAccount.Id);
            Assert.Equal(accountId, after.StorageAccount.Id);
            Assert.True(
                await patternContainer
                    .GetBlobClient(before.DataElement.BlobStoragePath)
                    .ExistsAsync()
            );
            Assert.False(
                await namedContainer.GetBlobClient(before.DataElement.BlobStoragePath).ExistsAsync()
            );
            Assert.True(
                await namedContainer.GetBlobClient(after.DataElement.BlobStoragePath).ExistsAsync()
            );
            Assert.False(
                await patternContainer
                    .GetBlobClient(after.DataElement.BlobStoragePath)
                    .ExistsAsync()
            );

            DataElementInternal readElementBefore = await _dataElementFixture.DataRepo.Read(
                instance.Id,
                before.DataElement.Id
            );
            DataElementInternal readElementAfter = await _dataElementFixture.DataRepo.Read(
                instance.Id,
                after.DataElement.Id
            );
            Assert.Null(readElementBefore.StorageAccountId);
            Assert.Equal(accountId, readElementAfter.StorageAccountId);

            Assert.Equal("before the row", await ReadContent(readBefore));
            Assert.Equal("after the row", await ReadContent(readAfter));

            InstanceQueryResult listed =
                await _dataElementFixture.InstanceRepo.GetInstancesFromQuery(
                    new InstanceQueryParameters
                    {
                        Size = 100,
                        AppId = instance.AppId,
                        IncludeDataElements = true,
                    },
                    CancellationToken.None
                );
            DataElementInternal listedElement = Assert.Single(
                Assert.Single(listed.Instances).Data,
                element => element.Id == after.DataElement.Id
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                resolver.ForElement(
                    listedElement,
                    org,
                    StorageAccountNaming.DefaultStorageAccountNumber
                )
            );
        }
        finally
        {
            await patternContainer.DeleteIfExistsAsync();
            await namedContainer.DeleteIfExistsAsync();
        }
    }

    private IStorageAccountResolver CreateResolver() =>
        TestStorageAccounts.CreateResolver(
            new PgStorageAccountRepository(_dataElementFixture.DataSource),
            _blobFixture.Configuration
        );

    private async Task<DataUploadResult> Upload(
        IStorageAccountResolver resolver,
        InstanceInternal instance,
        Application application,
        string content
    )
    {
        DataService dataService = new(
            Mock.Of<IFileScanQueueClient>(),
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            resolver
        );
        return await dataService.UploadDataAndCreateDataElement(
            instance,
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            new DataElementCreateOptions
            {
                DataElementId = Guid.NewGuid(),
                DataType = "default",
                ContentType = "text/plain",
                Filename = "content.txt",
                Created = DateTime.UtcNow,
                CreatedBy = instance.Org,
                IsRead = true,
            },
            instance.InternalId,
            application,
            cancellationToken: CancellationToken.None
        );
    }

    private DataController CreateDataController(
        IStorageAccountResolver resolver,
        Application application,
        string org
    )
    {
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(repository =>
                repository.FindOne(application.Id, application.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(application);
        Mock<IAuthorization> authorization = new();
        authorization
            .Setup(service =>
                service.AuthorizeEnrichedInstanceAction(It.IsAny<InstanceInternal>(), "read")
            )
            .ReturnsAsync(true);
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(AltinnCoreClaimTypes.Org, org),
                        new Claim(AltinnCoreClaimTypes.OrgNumber, "111111111"),
                    ],
                    "test"
                )
            ),
        };
        DataService dataService = new(
            Mock.Of<IFileScanQueueClient>(),
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            resolver
        );
        return new DataController(
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            _dataElementFixture.InstanceRepo,
            _dataElementFixture.InstanceMutationRepo,
            applicationRepository.Object,
            dataService,
            Mock.Of<IInstanceEventService>(),
            Options.Create(new GeneralSettings { Hostname = "https://altinn.no/" }),
            null,
            authorization.Object,
            resolver
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static async Task<string> ReadContent(ActionResult result)
    {
        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        using StreamReader reader = new(file.FileStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static Task<int> CountInstanceEvents(Guid instanceGuid, InstanceEventType eventType) =>
        PostgresUtil.RunCountQuery(
            $"""
            select count(*)
            from storage.instanceevents
            where instance = '{instanceGuid}'
              and event ->> 'EventType' = '{eventType}'
            """
        );
}
