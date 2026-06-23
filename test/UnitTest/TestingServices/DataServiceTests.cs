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

        InstanceInternal instance = CreateInstanceInternal(new Instance());
        DataType dataType = new DataType { EnableFileScan = false };
        DataElementInternal dataElement = new(new DataElement { }, null);
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

        InstanceInternal instance = CreateInstanceInternal(new Instance { Id = "343243/guid" });
        DataType dataType = new DataType { EnableFileScan = true };
        const string blobStoragePath = "app/instance/data-elements/blob-version-id";
        const string blobVersionId = "blob-version-id";
        DataElement dataElement = new() { BlobStoragePath = blobStoragePath };
        DataElementInternal dataElementInternal = new(dataElement, blobVersionId);
        DateTimeOffset blobTimestamp = DateTimeOffset.UtcNow;

        // Act
        await target.StartFileScan(
            instance,
            dataType,
            dataElementInternal,
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
                        && c.Contains(
                            "\"blobStoragePath\":\"app/instance/data-elements/blob-version-id\""
                        )
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
        Guid id = Guid.NewGuid();
        string blobStoragePath = "/ttd/some-app";
        string blobVersionId = "blob-version-id";
        string versionedBlobStoragePath = $"{blobStoragePath}/{blobVersionId}";
        byte[] blobStorageBytes = "whatever"u8.ToArray();
        string expectedHashResult =
            "85738f8f9a7f1b04b5329c590ebcb9e425925c6d0984089c43a022de4f19c281";

        DataElement dataElement = new DataElement
        {
            Id = id.ToString(),
            BlobStoragePath = versionedBlobStoragePath,
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new DataElementInternal(dataElement, blobVersionId));
        blobRepositoryMock
            .Setup(drm =>
                drm.ReadBlob("ttd", versionedBlobStoragePath, null, It.IsAny<CancellationToken>())
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
            drm =>
                drm.ReadBlob("ttd", versionedBlobStoragePath, null, It.IsAny<CancellationToken>()),
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
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new DataElementInternal(dataElement, null));
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

        DataElement dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "/ttd/some-app",
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new DataElementInternal(dataElement, null));

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
        const long expectedBlobSize = 123;
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
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((DataElementInternal de, long _, CancellationToken _) => de);

        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string expectedBlobStoragePath =
            $"ttd/some-app/{instanceGuid}/data-elements/{allocatedBlobVersionId}";
        Instance instance = new()
        {
            Id = $"123/{instanceGuid}",
            Org = "ttd",
            AppId = "ttd/some-app",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = dataElementId,
            DataType = "signing",
            ContentType = "application/json",
            Filename = "signing.json",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act
        await dataService.UploadDataAndCreateDataElement(
            CreateInstanceInternal(instance),
            new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
            options,
            0,
            null,
            CancellationToken.None
        );

        // Assert
        dataRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.Is<DataElementInternal>(de =>
                        de.DataElement.Size == expectedBlobSize
                        && de.DataElement.Id == dataElementId.ToString()
                        && de.DataElement.InstanceGuid == instanceGuid.ToString()
                        && de.DataElement.BlobStoragePath == expectedBlobStoragePath
                        && de.BlobVersionId == allocatedBlobVersionId
                    ),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_WriteBlobThrows_DeletesExplicitVersionBlobAllocation()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const int storageAccountNumber = 7;
        string expectedBlobStoragePath =
            $"ttd/some-app/{instanceGuid}/data-elements/{allocatedBlobVersionId}";
        List<string> cleanupCalls = [];

        dataRepositoryMock
            .Setup(drm =>
                drm.CreateBlobVersionId(
                    instanceGuid,
                    dataElementId,
                    "ttd/some-app",
                    "ttd",
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        blobRepositoryMock
            .Setup(drm =>
                drm.WriteBlob(
                    "ttd",
                    It.IsAny<Stream>(),
                    expectedBlobStoragePath,
                    storageAccountNumber
                )
            )
            .ThrowsAsync(new InvalidOperationException("blob write failed"));
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", expectedBlobStoragePath, storageAccountNumber))
            .Callback(() => cleanupCalls.Add("blob"))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersion(
                    dataElementId,
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => cleanupCalls.Add("row"))
            .ReturnsAsync(true);

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );
        Instance instance = new()
        {
            Id = $"123/{instanceGuid}",
            Org = "ttd",
            AppId = "ttd/some-app",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = dataElementId,
            DataType = "signing",
            ContentType = "application/json",
            Filename = "signing.json",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.UploadDataAndCreateDataElement(
                CreateInstanceInternal(instance),
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                options,
                0,
                storageAccountNumber,
                CancellationToken.None
            )
        );

        Assert.Equal("blob write failed", exception.Message);
        Assert.Equal(["blob", "row"], cleanupCalls);
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_ZeroLengthBlob_DeletesExplicitVersionBlobAllocation()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string expectedBlobStoragePath =
            $"ttd/some-app/{instanceGuid}/data-elements/{allocatedBlobVersionId}";
        List<string> cleanupCalls = [];

        dataRepositoryMock
            .Setup(drm =>
                drm.CreateBlobVersionId(
                    instanceGuid,
                    dataElementId,
                    "ttd/some-app",
                    "ttd",
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        blobRepositoryMock
            .Setup(drm => drm.WriteBlob("ttd", It.IsAny<Stream>(), expectedBlobStoragePath, null))
            .ReturnsAsync((0, DateTimeOffset.UtcNow));
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", expectedBlobStoragePath, null))
            .Callback(() => cleanupCalls.Add("blob"))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersion(
                    dataElementId,
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => cleanupCalls.Add("row"))
            .ReturnsAsync(true);

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );
        Instance instance = new()
        {
            Id = $"123/{instanceGuid}",
            Org = "ttd",
            AppId = "ttd/some-app",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = dataElementId,
            DataType = "signing",
            ContentType = "application/json",
            Filename = "signing.json",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            dataService.UploadDataAndCreateDataElement(
                CreateInstanceInternal(instance),
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                options,
                0,
                null,
                CancellationToken.None
            )
        );

        Assert.Equal("Empty stream provided. Cannot persist data.", exception.Message);
        Assert.Equal(["blob", "row"], cleanupCalls);
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_DoesNotDeleteExplicitVersionBlob()
    {
        // Arrange
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
            .ReturnsAsync((666, DateTimeOffset.Now));

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
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);

        Guid instanceGuid = Guid.NewGuid();
        Instance instance = new()
        {
            Id = $"123/{instanceGuid}",
            Org = "ttd",
            AppId = "ttd/some-app",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = Guid.NewGuid(),
            DataType = "signing",
            ContentType = "application/json",
            Filename = "signing.json",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        // Act/assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.UploadDataAndCreateDataElement(
                CreateInstanceInternal(instance),
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                options,
                0,
                null,
                CancellationToken.None
            )
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersion(
                    options.DataElementId,
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task UploadDataAndCreateDataElement_CreateThrows_StillThrowsOriginalException()
    {
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
            .ReturnsAsync((666, DateTimeOffset.Now));

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
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        Guid instanceGuid = Guid.NewGuid();
        Instance instance = new()
        {
            Id = $"123/{instanceGuid}",
            Org = "ttd",
            AppId = "ttd/some-app",
        };
        DataElementCreateOptions options = new()
        {
            DataElementId = Guid.NewGuid(),
            DataType = "signing",
            ContentType = "application/json",
            Filename = "signing.json",
            Created = DateTime.UtcNow,
            CreatedBy = "ttd",
        };

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceEventServiceMock.Object
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.UploadDataAndCreateDataElement(
                CreateInstanceInternal(instance),
                new MemoryStream(Encoding.UTF8.GetBytes("whatever")),
                options,
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
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        dataRepositoryMock
            .Setup(drm =>
                drm.Update(
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
        dataRepositoryMock
            .Setup(drm => drm.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm => drm.ReadBlobVersions(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());

        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
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
            instanceEventServiceMock.Object
        );

        Guid instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
        };
        var dataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = "ttd/app/instance-guid/data/element",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataService.DeleteImmediately(
                CreateInstanceInternal(instance),
                new DataElementInternal(dataElement, null),
                null
            )
        );

        Assert.Equal("cleanup failed", exception.Message);
        dataRepositoryMock.Verify(
            drm => drm.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        instanceEventServiceMock.Verify(
            ies =>
                ies.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task DeleteImmediately_MarkMetadataNotFound_DeletesBlobAndMetadata()
    {
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const string currentBlobStoragePath = "ttd/app/instance-guid/data/element";

        dataRepositoryMock
            .Setup(drm =>
                drm.Update(
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
        dataRepositoryMock
            .Setup(drm => drm.ReadBlobVersions(dataElementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());
        dataRepositoryMock
            .Setup(drm => drm.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", currentBlobStoragePath, null))
            .ReturnsAsync(true);
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
            instanceEventServiceMock.Object
        );
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = currentBlobStoragePath,
        };

        await dataService.DeleteImmediately(
            CreateInstanceInternal(instance),
            new DataElementInternal(dataElement, null),
            null
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", currentBlobStoragePath, null),
            Times.Once
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.Delete(
                    It.Is<DataElement>(de => de.Id == dataElement.Id),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        instanceEventServiceMock.Verify(
            ies =>
                ies.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.Is<Instance>(i => i.Id == instance.Id),
                    It.Is<DataElement>(de => de.Id == dataElement.Id)
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task DeleteImmediately_WithBlobVersions_DeletesVersionedBlobsAndLegacyBase()
    {
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<IInstanceEventService> instanceEventServiceMock = new Mock<IInstanceEventService>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const int blobStorageAccountNumber = 7;
        string legacyBlobStoragePath = $"ttd/app/{instanceGuid}/data/{dataElementId}";

        dataRepositoryMock
            .Setup(drm => drm.ReadBlobVersions(dataElementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [firstBlobVersionId, secondBlobVersionId]
                ),
            ]);
        dataRepositoryMock
            .Setup(drm => drm.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobs(
                    "storage-org",
                    It.Is<IEnumerable<string>>(paths =>
                        string.Join(",", paths)
                        == $"stored/app/{instanceGuid}/data-elements/{firstBlobVersionId},stored/app/{instanceGuid}/data-elements/{secondBlobVersionId}"
                    ),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null))
            .ReturnsAsync(true);
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
            instanceEventServiceMock.Object
        );
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = $"stored/app/{instanceGuid}/data-elements/{secondBlobVersionId}",
        };

        dataRepositoryMock
            .Setup(drm =>
                drm.Update(
                    instanceGuid,
                    dataElementId,
                    It.Is<Dictionary<string, object>>(properties =>
                        properties.ContainsKey("/deleteStatus")
                    ),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (
                    Guid _,
                    Guid _,
                    Dictionary<string, object> properties,
                    DataElementUpdateContext _,
                    CancellationToken _
                ) =>
                {
                    dataElement.DeleteStatus = (DeleteStatus)properties["/deleteStatus"];
                    return dataElement;
                }
            );

        await dataService.DeleteImmediately(
            CreateInstanceInternal(instance),
            new DataElementInternal(dataElement, secondBlobVersionId),
            null
        );

        blobRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobs(
                    "storage-org",
                    It.IsAny<IEnumerable<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null),
            Times.Once
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.Delete(
                    It.Is<DataElement>(de => de.Id == dataElement.Id),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        instanceEventServiceMock.Verify(
            ies =>
                ies.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.Is<Instance>(i => i.Id == instance.Id),
                    It.Is<DataElement>(de => de.Id == dataElement.Id)
                ),
            Times.Once
        );
    }

    private static InstanceInternal CreateInstanceInternal(Instance instance)
    {
        return new InstanceInternal(instance, []);
    }
}
