#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class DataServiceTests
{
    [Fact]
    public async Task PerformFileScanTest_EnableFileScanIsFalse_ScanIsNotqueued()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        DataService target = new DataService(
            fileScanMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        InstanceInternal instance = new InstanceInternal();
        DataType dataType = new DataType { EnableFileScan = false };
        DataElementInternal dataElement = new DataElementInternal { };
        DateTimeOffset blobTimestamp = DateTimeOffset.UtcNow;

        // Act
        await target.StartFileScan(
            instance,
            dataType,
            dataElement,
            blobTimestamp,
            null,
            CancellationToken.None
        );

        // Assert
        fileScanMock.Verify(
            f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never()
        );
    }

    [Fact]
    public async Task PerformFileScanTest_EnableFileScanIsTrue_ScanIsQueued()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        DataService target = new DataService(
            fileScanMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        InstanceInternal instance = new()
        {
            Id = "guid",
            InstanceOwner = new InstanceOwner { PartyId = "343243" },
        };
        DataType dataType = new DataType { EnableFileScan = true };
        DataElementInternal dataElement = new()
        {
            BlobStoragePath = "app/instance/data-elements/blob-version-id",
            BlobVersionId = "blob-version-id",
        };
        DateTimeOffset blobTimestamp = DateTimeOffset.UtcNow;

        // Act
        await target.StartFileScan(
            instance,
            dataType,
            dataElement,
            blobTimestamp,
            null,
            CancellationToken.None
        );

        // Assert
        fileScanMock.Verify(
            f =>
                f.EnqueueFileScan(
                    It.Is<string>(content =>
                        content.Contains("\"instanceId\":\"343243/guid\"")
                        && content.Contains(
                            "\"blobStoragePath\":\"app/instance/data-elements/blob-version-id\""
                        )
                        && content.Contains("\"blobVersionId\":\"blob-version-id\"")
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once()
        );
    }

    [Fact]
    public async Task GenerateSha256Hash_Success()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid id = Guid.NewGuid();
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string blobStoragePath = $"ttd/some-app/instance/data-elements/{blobVersionId}";
        byte[] blobStorageBytes = "whatever"u8.ToArray();
        string expectedHashResult =
            "85738f8f9a7f1b04b5329c590ebcb9e425925c6d0984089c43a022de4f19c281";

        DataElementInternal dataElement = new DataElementInternal
        {
            Id = id.ToString(),
            BlobStoragePath = blobStoragePath,
            BlobVersionId = blobVersionId,
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(dataElement);
        blobRepositoryMock
            .Setup(drm =>
                drm.ReadBlob(
                    It.IsAny<string>(),
                    blobStoragePath,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(blobStorageBytes));

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash(
            "ttd",
            Guid.NewGuid(),
            id,
            null
        );

        // Assert
        Assert.Equal(fileHash, expectedHashResult);
        Assert.Null(serviceError);
        dataRepositoryMock.VerifyAll();
        blobRepositoryMock.Verify(
            repository =>
                repository.ReadBlob("ttd", blobStoragePath, null, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GenerateSha256Hash_WithoutBlobVersionId_FallsBackToCurrentBlob()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string blobStoragePath = "/ttd/some-app";
        byte[] blobStorageBytes = "whatever"u8.ToArray();

        DataElementInternal dataElement = new()
        {
            Id = dataElementId.ToString(),
            BlobStoragePath = blobStoragePath,
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(dataElement);
        blobRepositoryMock
            .Setup(drm => drm.ReadBlob("ttd", blobStoragePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(blobStorageBytes));

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash(
            "ttd",
            instanceGuid,
            dataElementId,
            null
        );

        // Assert
        Assert.NotNull(fileHash);
        Assert.Null(serviceError);
        blobRepositoryMock.Verify(
            drm => drm.ReadBlob("ttd", blobStoragePath, null, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GenerateSha256Hash_Failed_DataElementNotExists()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash(
            "ttd",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null
        );

        // Assert
        Assert.Null(fileHash);
        Assert.Equal(404, serviceError.ErrorCode);
    }

    [Fact]
    public async Task GenerateSha256Hash_Failed_FiletNotExists()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        DataElementInternal dataElement = new DataElementInternal
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "/ttd/some-app",
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(dataElement);

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash(
            "ttd",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null
        );

        // Assert
        Assert.Null(fileHash);
        Assert.Equal(404, serviceError.ErrorCode);
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_Success()
    {
        // Arrange
        const long expectedBlobSize = 666;
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        blobRepositoryMock
            .Setup(drm =>
                drm.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((expectedBlobSize, DateTimeOffset.Now));

        dataRepositoryMock
            .Setup(drm =>
                drm.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepositoryMock
            .Setup(drm =>
                drm.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ReturnsAsync((DataElementInternal de, long _, CancellationToken _) => de);

        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string expectedBlobStoragePath =
            $"ttd/some-app/{instanceGuid}/data-elements/{allocatedBlobVersionId}";
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "ttd/some-app",
            Org = "ttd",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = dataElementId,
            DataType = "attachment",
            ContentType = "application/octet-stream",
            Filename = "file.bin",
            Created = DateTime.UtcNow,
            CreatedBy = "1337",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        (DataElementInternal created, _, _) = await dataService.UploadDataAndCreateDataElement(
            instance,
            new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
            options,
            0,
            null,
            CancellationToken.None
        );

        // Assert
        dataRepositoryMock.VerifyAll();
        Assert.Equal(allocatedBlobVersionId, created.BlobVersionId);
        Assert.Equal(expectedBlobSize, created.Size);
        Assert.Equal(expectedBlobStoragePath, created.BlobStoragePath);
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.Is<DataElementInternal>(de =>
                        de.Size == expectedBlobSize
                        && de.Id == dataElementId.ToString()
                        && de.InstanceGuid == instanceGuid.ToString()
                        && de.BlobStoragePath == expectedBlobStoragePath
                        && de.BlobVersionId == allocatedBlobVersionId
                    ),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_WriteBlobThrows_DeletesExplicitVersionBlobAllocation()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid dataElementId = Guid.NewGuid();
        const int storageAccountNumber = 7;
        InstanceInternal instance = CreateInstance();
        Guid instanceGuid = Guid.Parse(instance.Id);
        string expectedBlobStoragePath =
            $"{instance.AppId}/{instanceGuid}/data-elements/{allocatedBlobVersionId}";
        List<string> cleanupCalls = [];
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        dataRepository
            .Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    dataElementId,
                    instance.AppId,
                    instance.Org,
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepository
            .Setup(repository =>
                repository.DeleteBlobVersion(
                    dataElementId,
                    allocatedBlobVersionId,
                    CancellationToken.None
                )
            )
            .Callback(() => cleanupCalls.Add("row"))
            .ReturnsAsync(true);
        blobRepository
            .Setup(repository =>
                repository.WriteBlob(
                    instance.Org,
                    It.IsAny<Stream>(),
                    expectedBlobStoragePath,
                    storageAccountNumber
                )
            )
            .ThrowsAsync(new InvalidOperationException("blob write failed"));
        blobRepository
            .Setup(repository =>
                repository.DeleteBlob(instance.Org, expectedBlobStoragePath, storageAccountNumber)
            )
            .Callback(() => cleanupCalls.Add("blob"))
            .ReturnsAsync(true);
        DataService service = CreateDataService(dataRepository, blobRepository);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.UploadDataAndCreateDataElement(
                    instance,
                    new MemoryStream("content"u8.ToArray()),
                    CreateOptions(dataElementId),
                    0,
                    storageAccountNumber,
                    CancellationToken.None
                )
        );

        Assert.Equal("blob write failed", exception.Message);
        Assert.Equal(["blob", "row"], cleanupCalls);
        blobRepository.VerifyAll();
        dataRepository.VerifyAll();
        dataRepository.Verify(
            repository =>
                repository.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_ZeroLengthBlob_DeletesExplicitVersionBlobAllocation()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid dataElementId = Guid.NewGuid();
        List<string> cleanupCalls = [];
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        dataRepository
            .Setup(repository =>
                repository.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    dataElementId,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepository
            .Setup(repository =>
                repository.DeleteBlobVersion(
                    dataElementId,
                    allocatedBlobVersionId,
                    CancellationToken.None
                )
            )
            .Callback(() => cleanupCalls.Add("row"))
            .ReturnsAsync(true);
        blobRepository
            .Setup(repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((0L, DateTimeOffset.UtcNow));
        blobRepository
            .Setup(repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>())
            )
            .Callback(() => cleanupCalls.Add("blob"))
            .ReturnsAsync(true);
        DataService service = CreateDataService(dataRepository, blobRepository);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UploadDataAndCreateDataElement(
                CreateInstance(),
                new MemoryStream("content"u8.ToArray()),
                CreateOptions(dataElementId),
                0,
                null,
                CancellationToken.None
            )
        );

        Assert.Equal("Empty stream provided. Cannot persist data.", exception.Message);
        Assert.Equal(["blob", "row"], cleanupCalls);
        blobRepository.VerifyAll();
        dataRepository.VerifyAll();
        dataRepository.Verify(
            repository =>
                repository.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_DeletesAllocatedVersionBlob()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid dataElementId = Guid.NewGuid();
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        dataRepository
            .Setup(repository =>
                repository.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    dataElementId,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepository
            .Setup(repository =>
                repository.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));
        blobRepository
            .Setup(repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((123L, DateTimeOffset.UtcNow));
        DataService service = CreateDataService(dataRepository, blobRepository);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.UploadDataAndCreateDataElement(
                    CreateInstance(),
                    new MemoryStream("content"u8.ToArray()),
                    CreateOptions(dataElementId),
                    0,
                    null,
                    CancellationToken.None
                )
        );

        Assert.Equal("metadata create failed", exception.Message);
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    "ttd",
                    It.Is<string>(path =>
                        path.EndsWith($"/data-elements/{allocatedBlobVersionId}")
                    ),
                    null
                ),
            Times.Once
        );
        dataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    dataElementId,
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_StillThrowsOriginalException()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();

        blobRepository
            .Setup(repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((666, DateTimeOffset.Now));
        dataRepository
            .Setup(repository =>
                repository.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepository
            .Setup(repository =>
                repository.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        DataService dataService = CreateDataService(dataRepository, blobRepository);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                dataService.UploadDataAndCreateDataElement(
                    CreateInstance(),
                    new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                    CreateOptions(Guid.NewGuid()),
                    0,
                    null,
                    CancellationToken.None
                )
        );

        Assert.Equal("metadata create failed", exception.Message);
    }

    [Fact]
    public async Task DeleteImmediately_LegacyBlobDeleteThrows_Throws()
    {
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        Mock<IInstanceEventService> eventService = new();

        dataRepository
            .Setup(repository =>
                repository.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (
                    Guid _,
                    Guid _,
                    Dictionary<string, object> _,
                    DataElementUpdateContext _,
                    CancellationToken _
                ) => new DataElement()
            );
        dataRepository
            .Setup(repository =>
                repository.Delete(It.IsAny<DataElementInternal>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(true);
        dataRepository
            .Setup(repository =>
                repository.ReadBlobVersions(It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());
        blobRepository
            .Setup(repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>())
            )
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        Guid instanceGuid = Guid.NewGuid();
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElementInternal dataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = "ttd/app/instance-guid/data/element",
        };
        DataService dataService = new(
            Mock.Of<IFileScanQueueClient>(),
            dataRepository.Object,
            blobRepository.Object,
            eventService.Object
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                dataService.DeleteImmediately(instance, dataElement, null)
        );

        Assert.Equal("cleanup failed", exception.Message);
        dataRepository.Verify(
            repository =>
                repository.Delete(It.IsAny<DataElementInternal>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        eventService.Verify(
            service =>
                service.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task DeleteImmediately_MarkMetadataNotFound_DeletesBlobAndMetadata()
    {
        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const string currentBlobStoragePath = "ttd/app/instance-guid/data/element";
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        Mock<IInstanceEventService> eventService = new();

        dataRepository
            .Setup(repository =>
                repository.Update(
                    instanceGuid,
                    dataElementId,
                    It.Is<Dictionary<string, object>>(properties =>
                        properties.ContainsKey("/deleteStatus")
                    ),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new RepositoryException("Data element was not found.", HttpStatusCode.NotFound)
            );
        dataRepository
            .Setup(repository =>
                repository.ReadBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());
        dataRepository
            .Setup(repository =>
                repository.Delete(It.IsAny<DataElementInternal>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(false);
        blobRepository
            .Setup(repository => repository.DeleteBlob("ttd", currentBlobStoragePath, null))
            .ReturnsAsync(true);

        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElementInternal dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = currentBlobStoragePath,
        };
        eventService
            .Setup(service =>
                service.DispatchEvent(InstanceEventType.Deleted, instance, dataElement)
            )
            .Returns(Task.CompletedTask);
        DataService dataService = new(
            Mock.Of<IFileScanQueueClient>(),
            dataRepository.Object,
            blobRepository.Object,
            eventService.Object
        );

        await dataService.DeleteImmediately(instance, dataElement, null);

        blobRepository.Verify(
            repository => repository.DeleteBlob("ttd", currentBlobStoragePath, null),
            Times.Once
        );
        dataRepository.Verify(
            repository =>
                repository.Delete(
                    It.Is<DataElementInternal>(element => element.Id == dataElement.Id),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        eventService.Verify(
            service =>
                service.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.Is<InstanceInternal>(value => value.Id == instance.Id),
                    It.Is<DataElementInternal>(element => element.Id == dataElement.Id)
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task DeleteImmediately_WithBlobVersions_DeletesVersionedBlobsAndLegacyBase()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const string firstVersion = "first-version";
        const string secondVersion = "second-version";
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElementInternal dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = BlobRepository.GetVersionedBlobPath(
                instance.AppId,
                instanceGuid.ToString(),
                secondVersion
            ),
            BlobVersionId = secondVersion,
            LastChangedBy = "1337",
        };
        DataElementInternal marked = new()
        {
            Id = dataElement.Id,
            InstanceGuid = dataElement.InstanceGuid,
            BlobStoragePath = dataElement.BlobStoragePath,
            BlobVersionId = secondVersion,
            DeleteStatus = new DeleteStatus { IsHardDeleted = true },
        };
        Mock<IDataRepository> dataRepository = new();
        Mock<IBlobRepository> blobRepository = new();
        Mock<IInstanceEventService> eventService = new();
        dataRepository
            .Setup(repository =>
                repository.Update(
                    instanceGuid,
                    dataElementId,
                    It.IsAny<System.Collections.Generic.Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(marked);
        dataRepository
            .Setup(repository => repository.ReadBlobVersions(dataElementId, CancellationToken.None))
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    instance.AppId,
                    instance.Org,
                    null,
                    [firstVersion, secondVersion]
                ),
            ]);
        dataRepository
            .Setup(repository => repository.Delete(marked, CancellationToken.None))
            .ReturnsAsync(true);
        blobRepository
            .Setup(repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>())
            )
            .ReturnsAsync(true);
        eventService.Setup(service =>
            service.DispatchEvent(InstanceEventType.Deleted, instance, marked)
        );
        DataService service = new(
            Mock.Of<IFileScanQueueClient>(),
            dataRepository.Object,
            blobRepository.Object,
            eventService.Object
        );

        DataElementInternal deleted = await service.DeleteImmediately(instance, dataElement, null);

        Assert.Same(marked, deleted);
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    instance.Org,
                    BlobRepository.GetVersionedBlobPath(
                        instance.AppId,
                        instanceGuid.ToString(),
                        firstVersion
                    ),
                    null
                ),
            Times.Once
        );
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    instance.Org,
                    BlobRepository.GetVersionedBlobPath(
                        instance.AppId,
                        instanceGuid.ToString(),
                        secondVersion
                    ),
                    null
                ),
            Times.Once
        );
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    instance.Org,
                    $"{instance.AppId}/{instanceGuid}/data/{dataElementId}",
                    null
                ),
            Times.Once
        );
        dataRepository.VerifyAll();
        eventService.VerifyAll();
    }

    private static DataService CreateDataService(
        Mock<IDataRepository> dataRepository,
        Mock<IBlobRepository> blobRepository
    ) =>
        new(
            Mock.Of<IFileScanQueueClient>(),
            dataRepository.Object,
            blobRepository.Object,
            Mock.Of<IInstanceEventService>()
        );

    private static InstanceInternal CreateInstance() =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            AppId = "ttd/app",
            Org = "ttd",
        };

    private static DataElementCreateOptions CreateOptions(Guid dataElementId) =>
        new()
        {
            DataElementId = dataElementId,
            DataType = "attachment",
            ContentType = "application/octet-stream",
            Filename = "file.bin",
            Created = DateTime.UtcNow,
            CreatedBy = "1337",
        };
}
