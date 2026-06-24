#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class CleanupControllerUnitTests
{
    [Fact]
    public async Task CleanupInstances_DeletesCurrentAndVersionedPrefixesBeforeMetadata()
    {
        // Arrange
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string thirdBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const int storageAccountNumber = 7;
        const int blobStorageAccountNumber = 9;
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
            Data = [],
        };
        BlobVersionReferencesInternal blobVersion = new(
            instanceGuid,
            "stored/app",
            "storage-org",
            blobStorageAccountNumber,
            [firstBlobVersionId, secondBlobVersionId]
        );
        BlobVersionReferencesInternal alreadyDeletedByCurrentContext = new(
            instanceGuid,
            instance.AppId,
            instance.Org,
            storageAccountNumber,
            [thirdBlobVersionId]
        );

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();

        instanceRepositoryMock
            .Setup(repository => repository.GetHardDeletedInstances(It.IsAny<CancellationToken>()))
            .ReturnsAsync([instance]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetBlobVersionsForInstance(instanceGuid, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new Dictionary<Guid, List<BlobVersionReferencesInternal>>
                {
                    [dataElementId] = [blobVersion],
                    [Guid.NewGuid()] = [alreadyDeletedByCurrentContext],
                }
            );
        instanceRepositoryMock
            .Setup(repository => repository.Delete(instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        applicationRepositoryMock
            .Setup(repository => repository.FindAll())
            .ReturnsAsync([
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    AutoDeleteOnProcessEnd = false,
                },
            ]);
        applicationRepositoryMock
            .Setup(repository => repository.FindOne(instance.AppId, instance.Org, default))
            .ReturnsAsync(
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    StorageAccountNumber = storageAccountNumber,
                }
            );
        blobRepositoryMock
            .Setup(repository => repository.DeleteDataBlobs(instance, storageAccountNumber))
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteDataBlobs(
                    "storage-org",
                    "stored/app",
                    instanceGuid.ToString(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForInstance(instanceGuid.ToString(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(true);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupInstances(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
        instanceRepositoryMock.VerifyAll();
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteDataBlobs(
                    instance.Org,
                    instance.AppId,
                    instanceGuid.ToString(),
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlobs(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        instanceRepositoryMock.Verify(
            repository =>
                repository.GetOne(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupInstances_VersionedPrefixDeleteFails_DoesNotDeleteMetadata()
    {
        // Arrange
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const int storageAccountNumber = 7;
        const int blobStorageAccountNumber = 9;
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
            Data = [],
        };
        BlobVersionReferencesInternal blobVersion = new(
            instanceGuid,
            "stored/app",
            "storage-org",
            blobStorageAccountNumber,
            [blobVersionId]
        );

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();

        instanceRepositoryMock
            .Setup(repository => repository.GetHardDeletedInstances(It.IsAny<CancellationToken>()))
            .ReturnsAsync([instance]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetBlobVersionsForInstance(instanceGuid, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new Dictionary<Guid, List<BlobVersionReferencesInternal>>
                {
                    [dataElementId] = [blobVersion],
                }
            );
        applicationRepositoryMock
            .Setup(repository => repository.FindAll())
            .ReturnsAsync([
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    AutoDeleteOnProcessEnd = false,
                },
            ]);
        applicationRepositoryMock
            .Setup(repository => repository.FindOne(instance.AppId, instance.Org, default))
            .ReturnsAsync(
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    StorageAccountNumber = storageAccountNumber,
                }
            );
        blobRepositoryMock
            .Setup(repository => repository.DeleteDataBlobs(instance, storageAccountNumber))
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteDataBlobs(
                    "storage-org",
                    "stored/app",
                    instanceGuid.ToString(),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupInstances(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        blobRepositoryMock.VerifyAll();
        instanceRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            repository =>
                repository.DeleteForInstance(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        instanceRepositoryMock.Verify(
            repository => repository.Delete(It.IsAny<Instance>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDataelements_VersionedElement_DeletesAllVersionedBlobsAndLegacyBaseBlobBeforeMetadata()
    {
        // Arrange
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const int storageAccountNumber = 7;
        const int blobStorageAccountNumber = 9;
        string legacyBlobStoragePath = $"stored/app/{instanceGuid}/data/{dataElementId}";
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = BlobRepository.GetVersionedBlobPath(
                "stored/app",
                instanceGuid.ToString(),
                secondBlobVersionId
            ),
        };
        DeletedDataElementInternal deletedDataElement = new(
            new DataElementInternal(dataElement, secondBlobVersionId),
            [
                new BlobVersionReferencesInternal(
                    instanceGuid,
                    "stored/app",
                    "storage-org",
                    blobStorageAccountNumber,
                    [firstBlobVersionId, secondBlobVersionId]
                ),
            ]
        );
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            AppId = "ttd/app",
            Org = "ttd",
        };

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();
        string[] expectedBlobStoragePaths =
        [
            BlobRepository.GetVersionedBlobPath(
                "stored/app",
                instanceGuid.ToString(),
                firstBlobVersionId
            ),
            BlobRepository.GetVersionedBlobPath(
                "stored/app",
                instanceGuid.ToString(),
                secondBlobVersionId
            ),
        ];
        int callOrder = 0;

        instanceRepositoryMock
            .Setup(repository =>
                repository.GetHardDeletedDataElements(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([deletedDataElement]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOrphanBlobVersionsForCleanup(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((new InstanceInternal(instance, []), 0));
        applicationRepositoryMock
            .Setup(repository =>
                repository.FindOne(instance.AppId, instance.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    StorageAccountNumber = storageAccountNumber,
                }
            );
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlobs(
                    "storage-org",
                    It.Is<IEnumerable<string>>(paths =>
                        paths.SequenceEqual(expectedBlobStoragePaths)
                    ),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(0, callOrder++))
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlob(
                    "storage-org",
                    legacyBlobStoragePath,
                    blobStorageAccountNumber
                )
            )
            .Callback(() => Assert.Equal(1, callOrder++))
            .ReturnsAsync(false);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForCleanup(dataElement, It.IsAny<CancellationToken>())
            )
            .Callback(() => Assert.Equal(2, callOrder++))
            .ReturnsAsync(true);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupDataelements(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        Assert.Equal(3, callOrder);
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            repository => repository.Delete(dataElement, It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDataelements_MissingInstance_SkipsDataElement()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = $"ttd/app/{instanceGuid}/data/{dataElementId}",
        };
        DeletedDataElementInternal deletedDataElement = new(
            new DataElementInternal(dataElement, null),
            []
        );

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();

        instanceRepositoryMock
            .Setup(repository =>
                repository.GetHardDeletedDataElements(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([deletedDataElement]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((null, 0));
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOrphanBlobVersionsForCleanup(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupDataelements(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        applicationRepositoryMock.Verify(
            repository =>
                repository.FindOne(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataRepositoryMock.Verify(
            repository => repository.DeleteForCleanup(dataElement, It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDataelements_OrphanBlobVersions_DeletesVersionedBlobsBeforeMetadata()
    {
        // Arrange
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();
        const int storageAccountNumber = 7;
        BlobVersionReferencesInternal orphanBlobVersion = new(
            instanceGuid,
            "ttd/app",
            "storage-org",
            storageAccountNumber,
            [firstBlobVersionId, secondBlobVersionId]
        );
        string[] expectedBlobStoragePaths =
        [
            BlobRepository.GetVersionedBlobPath(
                "ttd/app",
                instanceGuid.ToString(),
                firstBlobVersionId
            ),
            BlobRepository.GetVersionedBlobPath(
                "ttd/app",
                instanceGuid.ToString(),
                secondBlobVersionId
            ),
        ];
        int callOrder = 0;

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();

        instanceRepositoryMock
            .Setup(repository =>
                repository.GetHardDeletedDataElements(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOrphanBlobVersionsForCleanup(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([orphanBlobVersion]);
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlobs(
                    "storage-org",
                    It.Is<IEnumerable<string>>(paths =>
                        paths.SequenceEqual(expectedBlobStoragePaths)
                    ),
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(0, callOrder++))
            .ReturnsAsync(false);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteOrphanBlobVersions(
                    It.Is<IReadOnlyList<string>>(versions =>
                        versions.SequenceEqual(new[] { firstBlobVersionId, secondBlobVersionId })
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(1, callOrder++))
            .ReturnsAsync(2);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupDataelements(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        Assert.Equal(2, callOrder);
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            repository =>
                repository.DeleteForCleanup(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
