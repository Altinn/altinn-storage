using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        Instance instance = new Instance();
        DataType dataType = new DataType { EnableFileScan = false };
        DataElement dataElement = new DataElement { };
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
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        Instance instance = new Instance { Id = "343243/guid" };
        DataType dataType = new DataType { EnableFileScan = true };
        DataElement dataElement = new DataElement { BlobVersionId = "blob-version-id" };
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
                    It.Is<string>(c =>
                        c.Contains($"\"instanceId\":\"343243/guid\"")
                        && c.Contains("\"blobVersionId\":\"blob-version-id\"")
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
        Mock<ILogger<DataService>> loggerMock = new Mock<ILogger<DataService>>();

        Guid id = Guid.NewGuid();
        string blobStoragePath = "/ttd/some-app";
        string blobVersionId = "blob-version-id";
        byte[] blobStorageBytes = "whatever"u8.ToArray();
        string expectedHashResult =
            "85738f8f9a7f1b04b5329c590ebcb9e425925c6d0984089c43a022de4f19c281";

        DataElement dataElement = new DataElement
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
                    "ttd",
                    blobStoragePath,
                    null,
                    blobVersionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(blobStorageBytes));

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            loggerMock.Object
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
            drm =>
                drm.ReadBlob(
                    "ttd",
                    blobStoragePath,
                    null,
                    blobVersionId,
                    It.IsAny<CancellationToken>()
                ),
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

        DataElement dataElement = new DataElement
        {
            Id = dataElementId.ToString(),
            BlobStoragePath = blobStoragePath,
            BlobVersionId = null,
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(dataElement);
        blobRepositoryMock
            .Setup(drm =>
                drm.ReadBlob("ttd", blobStoragePath, null, null, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new MemoryStream(blobStorageBytes));

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
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
            drm => drm.ReadBlob("ttd", blobStoragePath, null, null, It.IsAny<CancellationToken>()),
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
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
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

        DataElement dataElement = new DataElement
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
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
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
        const long expectedBlobSize = 123;
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
            .ReturnsAsync((expectedBlobSize, DateTimeOffset.Now, "mock-version-id"));

        dataRepositoryMock
            .Setup(drm =>
                drm.Create(It.IsAny<DataElement>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((DataElement de, long _, CancellationToken _) => de);

        DataElement dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "/ttd/some-app",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        // Act
        await dataService.UploadDataAndCreateDataElement(
            "ttd",
            new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
            dataElement,
            0,
            null
        );

        // Assert
        dataRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.Is<DataElement>(de =>
                        de.Size == expectedBlobSize && de.BlobVersionId == "mock-version-id"
                    ),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_CleansUpBlob()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        string blobVersionId = "mock-version-id";

        blobRepositoryMock
            .Setup(drm =>
                drm.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((666, DateTimeOffset.Now, blobVersionId));

        dataRepositoryMock
            .Setup(drm =>
                drm.Create(It.IsAny<DataElement>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", "/ttd/some-app", null, blobVersionId))
            .ReturnsAsync(true);

        DataElement dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "/ttd/some-app",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        // Act/assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.UploadDataAndCreateDataElement(
                "ttd",
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                dataElement,
                0,
                null
            )
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", "/ttd/some-app", null, blobVersionId),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_DeleteBlobThrows_StillThrowsOriginalException()
    {
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        string blobVersionId = "mock-version-id";

        blobRepositoryMock
            .Setup(drm =>
                drm.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((666, DateTimeOffset.Now, blobVersionId));

        dataRepositoryMock
            .Setup(drm =>
                drm.Create(It.IsAny<DataElement>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", "/ttd/some-app", null, blobVersionId))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        DataElement dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = Guid.NewGuid().ToString(),
            BlobStoragePath = "/ttd/some-app",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.UploadDataAndCreateDataElement(
                "ttd",
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                dataElement,
                0,
                null
            )
        );

        Assert.Equal("metadata create failed", exception.Message);
    }

    [Fact]
    public async Task DeleteImmediately_DeleteBlobThrows_StillReturnsOk()
    {
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        dataRepositoryMock
            .Setup(drm => drm.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), null)
            )
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        instanceEventServiceMock
            .Setup(ies =>
                ies.DispatchEvent(
                    It.Is<InstanceEventType>(t => t == InstanceEventType.Deleted),
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>()
                )
            )
            .Returns(Task.CompletedTask);

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object,
            NullLogger<DataService>.Instance
        );

        var instance = new Instance
        {
            Id = "1337/instance-guid",
            AppId = "ttd/app",
            Org = "ttd",
        };
        var dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = "instance-guid",
        };

        var result = await dataService.DeleteImmediately(instance, dataElement, null);

        Assert.Equal(dataElement, result);
        instanceEventServiceMock.VerifyAll();
    }
}
