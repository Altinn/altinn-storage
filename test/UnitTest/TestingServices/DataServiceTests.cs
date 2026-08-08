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
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
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

        DataService target = new DataService(
            fileScanMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object
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

        DataService target = new DataService(
            fileScanMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object
        );

        InstanceInternal instance = new()
        {
            Id = new Guid("0f9c4e1a-2b3d-4c5e-8f60-718293a4b5c6"),
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
                        content.Contains(
                            "\"instanceId\":\"343243/0f9c4e1a-2b3d-4c5e-8f60-718293a4b5c6\""
                        )
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

        Guid id = Guid.NewGuid();
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string blobStoragePath = $"ttd/some-app/instance/data-elements/{blobVersionId}";
        byte[] blobStorageBytes = "whatever"u8.ToArray();
        string expectedHashResult =
            "85738f8f9a7f1b04b5329c590ebcb9e425925c6d0984089c43a022de4f19c281";

        DataElementInternal dataElement = new DataElementInternal
        {
            Id = id,
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
            blobRepositoryMock.Object
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
    public async Task GenerateSha256Hash_Failed_DataElementNotExists()
    {
        // Arrange
        Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        DataService dataService = new DataService(
            fileScanQueueClientMock.Object,
            dataRepositoryMock.Object,
            blobRepositoryMock.Object
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

        DataElementInternal dataElement = new DataElementInternal
        {
            Id = Guid.NewGuid(),
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
            blobRepositoryMock.Object
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
            Id = instanceGuid,
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
            blobRepositoryMock.Object
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
        Assert.Empty(created.References);
        dataRepositoryMock.Verify(
            drm =>
                drm.Create(
                    It.Is<DataElementInternal>(de =>
                        de.Size == expectedBlobSize
                        && de.Id == dataElementId
                        && de.InstanceGuid == instanceGuid
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
        Guid instanceGuid = instance.Id;
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
    public async Task UploadDataAndCreateDataElement_CreateThrowsDefiniteRollback_DeletesAllocatedVersionBlob()
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
            .ThrowsAsync(new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01"));
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

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            service.UploadDataAndCreateDataElement(
                CreateInstance(),
                new MemoryStream("content"u8.ToArray()),
                CreateOptions(dataElementId),
                0,
                null,
                CancellationToken.None
            )
        );

        Assert.Equal("40P01", exception.SqlState);
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
    public async Task UploadDataAndCreateDataElement_CreateOutcomeUnknown_LeavesBlobForOrphanCleanup()
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
            .ThrowsAsync(new TimeoutException("commit outcome unknown"));
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

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            service.UploadDataAndCreateDataElement(
                CreateInstance(),
                new MemoryStream("content"u8.ToArray()),
                CreateOptions(dataElementId),
                0,
                null,
                CancellationToken.None
            )
        );

        Assert.Equal("commit outcome unknown", exception.Message);
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
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
    public async Task CleanupDeletedDataElementBlobs_LegacyBlobDeleteThrows_DoesNotThrow()
    {
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());

        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);

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

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(null),
            null
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", dataElement.BlobStoragePath, null),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_DeletesLegacyBlob()
    {
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const string currentBlobStoragePath = "ttd/app/instance-guid/data/element";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", currentBlobStoragePath, null))
            .ReturnsAsync(true);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
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

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(null),
            null
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", currentBlobStoragePath, null),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_DetachedBlobVersionReadThrows_StillDeletesLegacyBlob()
    {
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        string legacyBlobStoragePath = $"ttd/app/{instanceGuid}/data/{dataElementId}";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new InvalidOperationException("read failed"));

        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
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
            BlobStoragePath = legacyBlobStoragePath,
        };

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(null),
            null
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_WithDetachedBlobVersions_AttemptsAllPhysicalDeletesBeforeBatchMetadataDelete()
    {
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> callOrder = [];
        IReadOnlyList<string> deletedMetadataIds = [];
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const int blobStorageAccountNumber = 7;
        string legacyBlobStoragePath = $"ttd/app/{instanceGuid}/data/{dataElementId}";
        string firstBlobStoragePath =
            $"stored/app/{instanceGuid}/data-elements/{firstBlobVersionId}";
        string secondBlobStoragePath =
            $"stored/app/{instanceGuid}/data-elements/{secondBlobVersionId}";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [firstBlobVersionId, secondBlobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, IReadOnlyList<string>, int?, CancellationToken>(
                (_, blobStoragePaths, _, _) =>
                {
                    Assert.Equal([firstBlobStoragePath, secondBlobStoragePath], blobStoragePaths);
                    callOrder.Add("blob-batch");
                }
            )
            .ReturnsAsync([true, true]);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, IReadOnlyList<string>, CancellationToken>(
                (_, blobVersionIds, _) =>
                {
                    deletedMetadataIds = [.. blobVersionIds];
                    callOrder.Add("metadata");
                }
            )
            .ReturnsAsync(2);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
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

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(secondBlobVersionId),
            null
        );

        blobRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobsIfExists(
                    "storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        Assert.Equal(["blob-batch", "metadata"], callOrder);
        Assert.Equal([firstBlobVersionId, secondBlobVersionId], deletedMetadataIds);
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_MultipleStorageGroups_PreservesGroupMembershipAndOrder()
    {
        string duplicateBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string firstGroupBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondGroupBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid firstInstanceGuid = Guid.NewGuid();
        Guid secondInstanceGuid = Guid.NewGuid();
        Guid thirdInstanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        List<string> callOrder = [];
        IReadOnlyList<string> deletedMetadataIds = [];
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        string firstDuplicateBlobStoragePath =
            $"first/app/{firstInstanceGuid}/data-elements/{duplicateBlobVersionId}";
        string secondGroupBlobStoragePath =
            $"second/app/{secondInstanceGuid}/data-elements/{secondGroupBlobVersionId}";
        string firstGroupBlobStoragePath =
            $"third/app/{thirdInstanceGuid}/data-elements/{firstGroupBlobVersionId}";
        string secondDuplicateBlobStoragePath =
            $"third/app/{thirdInstanceGuid}/data-elements/{duplicateBlobVersionId}";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    firstInstanceGuid,
                    "first/app",
                    "first-storage-org",
                    7,
                    [duplicateBlobVersionId]
                ),
                new BlobVersionReferencesInternal(
                    secondInstanceGuid,
                    "second/app",
                    "second-storage-org",
                    8,
                    [secondGroupBlobVersionId]
                ),
                new BlobVersionReferencesInternal(
                    thirdInstanceGuid,
                    "third/app",
                    "first-storage-org",
                    7,
                    [firstGroupBlobVersionId, duplicateBlobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "first-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, IReadOnlyList<string>, int?, CancellationToken>(
                (_, blobStoragePaths, _, _) =>
                {
                    Assert.Equal(
                        [
                            firstDuplicateBlobStoragePath,
                            firstGroupBlobStoragePath,
                            secondDuplicateBlobStoragePath,
                        ],
                        blobStoragePaths
                    );
                    callOrder.Add("first-storage");
                }
            )
            .ReturnsAsync([true, true, true]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "second-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    8,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, IReadOnlyList<string>, int?, CancellationToken>(
                (_, blobStoragePaths, _, _) =>
                {
                    Assert.Equal([secondGroupBlobStoragePath], blobStoragePaths);
                    callOrder.Add("second-storage");
                }
            )
            .ReturnsAsync([true]);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, IReadOnlyList<string>, CancellationToken>(
                (_, blobVersionIds, _) =>
                {
                    deletedMetadataIds = [.. blobVersionIds];
                    callOrder.Add("metadata");
                }
            )
            .ReturnsAsync(4);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(true);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
        Instance instance = new()
        {
            Id = $"1337/{firstInstanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
        };
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = firstInstanceGuid.ToString(),
            BlobStoragePath = firstDuplicateBlobStoragePath,
        };

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(duplicateBlobVersionId),
            null
        );

        Assert.Equal(["first-storage", "second-storage", "metadata"], callOrder);
        Assert.Equal(
            [
                duplicateBlobVersionId,
                firstGroupBlobVersionId,
                duplicateBlobVersionId,
                secondGroupBlobVersionId,
            ],
            deletedMetadataIds
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_CanceledAfterRead_DoesNotStartDeleteIo()
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        using CancellationTokenSource cancellationTokenSource = new();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, cancellationTokenSource.Token)
            )
            .Callback(() => cancellationTokenSource.Cancel())
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    7,
                    [blobVersionId]
                ),
            ]);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
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
            BlobStoragePath = $"stored/app/{instanceGuid}/data-elements/{blobVersionId}",
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dataService.CleanupDeletedDataElementBlobs(
                instance.FromApiModel(),
                dataElement.FromApiModel(blobVersionId),
                null,
                cancellationTokenSource.Token
            )
        );

        blobRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobsIfExists(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersions(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_CanceledAfterFirstStorageGroup_DoesNotStartLaterDeleteIo()
    {
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid dataElementId = Guid.NewGuid();
        InstanceInternal instance = CreateInstance();
        Guid instanceGuid = instance.Id;
        using CancellationTokenSource cancellationTokenSource = new();
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, cancellationTokenSource.Token)
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    instance.AppId,
                    "first-storage-org",
                    7,
                    [firstBlobVersionId]
                ),
                new BlobVersionReferencesInternal(
                    Guid.NewGuid(),
                    "second/app",
                    "second-storage-org",
                    8,
                    [secondBlobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "first-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    7,
                    cancellationTokenSource.Token
                )
            )
            .Callback(() => cancellationTokenSource.Cancel())
            .ReturnsAsync([true]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "second-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    8,
                    cancellationTokenSource.Token
                )
            )
            .ReturnsAsync([true]);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    cancellationTokenSource.Token
                )
            )
            .ReturnsAsync(2);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(true);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
        DataElementInternal dataElement = new()
        {
            Id = dataElementId,
            BlobVersionId = firstBlobVersionId,
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dataService.CleanupDeletedDataElementBlobs(
                instance,
                dataElement,
                null,
                cancellationTokenSource.Token
            )
        );

        blobRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobsIfExists(
                    "first-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    7,
                    cancellationTokenSource.Token
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobsIfExists(
                    "second-storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    8,
                    cancellationTokenSource.Token
                ),
            Times.Never
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersions(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_DetachedBlobBatchPartialSuccess_LeavesFailedIdOutOfBatchMetadataDelete()
    {
        string failedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string successfulBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        IReadOnlyList<string> deletedMetadataIds = [];
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const int blobStorageAccountNumber = 7;
        string failedBlobStoragePath =
            $"stored/app/{instanceGuid}/data-elements/{failedBlobVersionId}";
        string successfulBlobStoragePath =
            $"stored/app/{instanceGuid}/data-elements/{successfulBlobVersionId}";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [failedBlobVersionId, successfulBlobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, IReadOnlyList<string>, int?, CancellationToken>(
                (_, blobStoragePaths, _, _) =>
                    Assert.Equal(
                        [failedBlobStoragePath, successfulBlobStoragePath],
                        blobStoragePaths
                    )
            )
            .ReturnsAsync([false, true]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlob("ttd", $"ttd/app/{instanceGuid}/data/{dataElementId}", null)
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, IReadOnlyList<string>, CancellationToken>(
                (_, blobVersionIds, _) => deletedMetadataIds = [.. blobVersionIds]
            )
            .ReturnsAsync(1);
        DataService dataService = CreateDataService(dataRepositoryMock, blobRepositoryMock);
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
            BlobStoragePath = $"stored/app/{instanceGuid}/data-elements/{successfulBlobVersionId}",
        };

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(successfulBlobVersionId),
            null
        );

        Assert.Equal([successfulBlobVersionId], deletedMetadataIds);
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_DetachedBlobBatchDeleteThrows_DoesNotDeleteMetadataRows()
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        InvalidOperationException batchException = new("batch submit failed");
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<ILogger<DataService>> loggerMock = new Mock<ILogger<DataService>>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const int blobStorageAccountNumber = 7;

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [blobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(batchException);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlob("ttd", $"ttd/app/{instanceGuid}/data/{dataElementId}", null)
            )
            .ReturnsAsync(true);
        DataService dataService = CreateDataService(
            dataRepositoryMock,
            blobRepositoryMock,
            loggerMock
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
            BlobStoragePath = $"stored/app/{instanceGuid}/data-elements/{blobVersionId}",
        };

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(blobVersionId),
            null
        );

        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersions(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        dataRepositoryMock.Verify(
            drm =>
                drm.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        loggerMock.Verify(
            logger =>
                logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((_, _) => true),
                    batchException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupDeletedDataElementBlobs_DetachedMetadataBatchDeleteThrows_LogsAndContinues()
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        InvalidOperationException metadataException = new("metadata delete failed");
        Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
        Mock<IBlobRepository> blobRepositoryMock = new Mock<IBlobRepository>();
        Mock<ILogger<DataService>> loggerMock = new Mock<ILogger<DataService>>();

        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();
        const int blobStorageAccountNumber = 7;
        string legacyBlobStoragePath = $"ttd/app/{instanceGuid}/data/{dataElementId}";
        string blobStoragePath = $"stored/app/{instanceGuid}/data-elements/{blobVersionId}";

        dataRepositoryMock
            .Setup(drm =>
                drm.ReadDetachedBlobVersions(dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [blobVersionId]
                ),
            ]);
        blobRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobsIfExists(
                    "storage-org",
                    It.IsAny<IReadOnlyList<string>>(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([true]);
        blobRepositoryMock
            .Setup(drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(drm =>
                drm.DeleteBlobVersions(
                    dataElementId,
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(metadataException);
        DataService dataService = CreateDataService(
            dataRepositoryMock,
            blobRepositoryMock,
            loggerMock
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
            BlobStoragePath = $"stored/app/{instanceGuid}/data-elements/{blobVersionId}",
        };

        await dataService.CleanupDeletedDataElementBlobs(
            instance.FromApiModel(),
            dataElement.FromApiModel(blobVersionId),
            null
        );

        blobRepositoryMock.Verify(
            drm => drm.DeleteBlob("ttd", legacyBlobStoragePath, null),
            Times.Once
        );
        loggerMock.Verify(
            logger =>
                logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((_, _) => true),
                    metadataException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    public static TheoryData<Exception, bool> RollbackClassificationData =>
        new()
        {
            { new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01"), true },
            {
                new OperationCanceledException(
                    "canceled",
                    new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01")
                ),
                true
            },
            {
                new AggregateException(
                    new TimeoutException("timeout"),
                    new PostgresException("unique violation", "ERROR", "ERROR", "23505")
                ),
                false
            },
            {
                new AggregateException(
                    new PostgresException("unique violation", "ERROR", "ERROR", "23505"),
                    new RepositoryException("instance is deleted", HttpStatusCode.NotFound)
                ),
                true
            },
            {
                new AggregateException(
                    new AggregateException(
                        new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01")
                    )
                ),
                true
            },
            {
                new AggregateException(
                    new OperationCanceledException(
                        "canceled",
                        new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01")
                    )
                ),
                true
            },
            {
                new AggregateException(
                    new TimeoutException("timeout"),
                    new NpgsqlException("broken connection")
                ),
                false
            },
            {
                new AggregateException(
                    new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01"),
                    new AggregateException(new TimeoutException("timeout"))
                ),
                false
            },
            { new AggregateException(), false },
            { new RepositoryException("instance is deleted", HttpStatusCode.NotFound), true },
            { new InstanceVersionMismatchException(8, 3), true },
            { new ProcessStateVersionMismatchException(8, 3), true },
            { new DataElementBlobVersionMismatchException("blob version mismatch", 8, 3), true },
            { new OperationCanceledException("canceled"), false },
            { new TaskCanceledException("canceled"), false },
            { new TimeoutException("timed out"), false },
            { new ObjectDisposedException("connection"), false },
            { new NpgsqlException("broken connection"), false },
            { new InvalidOperationException("unknown failure"), false },
        };

    [Theory]
    [MemberData(nameof(RollbackClassificationData))]
    public void IndicatesDefiniteRollback_ClassifiesExceptionChain(
        Exception exception,
        bool expectedDefiniteRollback
    )
    {
        Assert.Equal(expectedDefiniteRollback, DataService.IndicatesDefiniteRollback(exception));
    }

    private static DataService CreateDataService(
        Mock<IDataRepository> dataRepository,
        Mock<IBlobRepository> blobRepository,
        Mock<ILogger<DataService>>? logger = null
    ) =>
        new(
            Mock.Of<IFileScanQueueClient>(),
            dataRepository.Object,
            blobRepository.Object,
            logger?.Object!
        );

    private static InstanceInternal CreateInstance() =>
        new()
        {
            Id = Guid.NewGuid(),
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
