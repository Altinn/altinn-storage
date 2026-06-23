#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
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
            "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Org = BlobRepositoryAzuriteFixture.Org;
        instance.AppId = $"{BlobRepositoryAzuriteFixture.Org}/test-applikasjon-1";
        Instance createdInstance = _dataElementFixture
            .InstanceRepo.Create(instance, CancellationToken.None)
            .Result;
        (_instanceInternal, _instanceInternalId) = _dataElementFixture
            .InstanceRepo.GetOne(
                Guid.Parse(createdInstance.Id.Split('/').Last()),
                false,
                CancellationToken.None
            )
            .Result;
    }

    [Fact]
    public async Task UploadAndDelete_WithPostgresAndAzurite_PersistsAndRemovesMetadataAndBlob()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new();
        Mock<IInstanceEventService> instanceEventServiceMock = new();
        instanceEventServiceMock
            .Setup(ies =>
                ies.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>()
                )
            )
            .Returns(Task.CompletedTask);

        DataService dataService = new(
            fileScanQueueClientMock.Object,
            _dataElementFixture.DataRepo,
            _blobFixture.Repository,
            instanceEventServiceMock.Object
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
        (DataElementInternal createdDataElement, DateTimeOffset blobTimestamp) =
            await dataService.UploadDataAndCreateDataElement(
                _instanceInternal,
                new MemoryStream(Encoding.UTF8.GetBytes(content)),
                options,
                _instanceInternalId,
                null,
                CancellationToken.None
            );

        // Assert upload
        Assert.NotEqual(default, blobTimestamp);
        Assert.False(string.IsNullOrEmpty(createdDataElement.BlobVersionId));
        Assert.EndsWith(
            $"/data-elements/{createdDataElement.BlobVersionId}",
            createdDataElement.DataElement.BlobStoragePath,
            StringComparison.Ordinal
        );

        DataElementInternal readDataElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(createdDataElement.DataElement.InstanceGuid),
            dataElementId,
            CancellationToken.None
        );
        Assert.Equal(createdDataElement.BlobVersionId, readDataElement.BlobVersionId);

        using Stream readBlob = await _blobFixture.Repository.ReadBlob(
            _instanceInternal.Instance.Org,
            createdDataElement.DataElement.BlobStoragePath,
            null,
            CancellationToken.None
        );
        using StreamReader reader = new(readBlob, Encoding.UTF8);
        Assert.Equal(content, await reader.ReadToEndAsync());
        Assert.True(await _blobFixture.Exists(createdDataElement.DataElement.BlobStoragePath));
        Assert.Single(await _dataElementFixture.DataRepo.ReadBlobVersions(dataElementId));

        // Act delete
        await dataService.DeleteImmediately(_instanceInternal, createdDataElement, null);

        // Assert delete
        Assert.Null(
            await _dataElementFixture.DataRepo.Read(
                Guid.Parse(createdDataElement.DataElement.InstanceGuid),
                dataElementId,
                CancellationToken.None
            )
        );
        Assert.Empty(await _dataElementFixture.DataRepo.ReadBlobVersions(dataElementId));
        Assert.False(await _blobFixture.Exists(createdDataElement.DataElement.BlobStoragePath));
        instanceEventServiceMock.Verify(
            ies =>
                ies.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.Is<Instance>(i => i.Id == _instanceInternal.Instance.Id),
                    It.Is<DataElement>(de => de.Id == dataElementId.ToString())
                ),
            Times.Once
        );
    }
}
