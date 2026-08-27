#nullable disable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
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
            _blobFixture.Repository
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
        (DataElementInternal createdDataElement, DateTimeOffset blobTimestamp, _) =
            await dataService.UploadDataAndCreateDataElement(
                _instanceInternal,
                new MemoryStream(Encoding.UTF8.GetBytes(content)),
                options,
                _instanceInternalId,
                null,
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
            _instanceInternal.Org,
            createdDataElement.BlobStoragePath,
            null,
            CancellationToken.None
        );
        using StreamReader reader = new(readBlob, Encoding.UTF8);
        Assert.Equal(content, await reader.ReadToEndAsync());
        Assert.True(await _blobFixture.Exists(createdDataElement.BlobStoragePath));
        Assert.Single(await _dataElementFixture.DataRepo.ReadBlobVersions(dataElementId));

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
            null,
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
        Assert.Empty(await _dataElementFixture.DataRepo.ReadBlobVersions(dataElementId));
        Assert.False(await _blobFixture.Exists(createdDataElement.BlobStoragePath));
        Assert.Equal(1, await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted));
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
