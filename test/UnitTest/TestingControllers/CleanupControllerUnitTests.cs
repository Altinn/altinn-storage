#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        const int storageAccountNumber = 7;
        const int blobStorageAccountNumber = 9;
        string storageInstanceId = instanceGuid.ToString().ToUpperInvariant();
        InstanceInternal instance = new()
        {
            Id = storageInstanceId,
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
            .ReturnsAsync([blobVersion, alreadyDeletedByCurrentContext]);
        instanceRepositoryMock
            .Setup(repository => repository.Delete(instanceGuid, It.IsAny<CancellationToken>()))
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
            .Setup(repository =>
                repository.DeleteDataBlobs(
                    instance.Org,
                    instance.AppId,
                    instance.Id,
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
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
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
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
                repository.DeleteBlobsIfExists(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
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
        const int storageAccountNumber = 7;
        const int blobStorageAccountNumber = 9;
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
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
            .ReturnsAsync([blobVersion]);
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
            .Setup(repository =>
                repository.DeleteDataBlobs(
                    instance.Org,
                    instance.AppId,
                    instance.Id,
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
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
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
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
            repository => repository.Delete(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupInstancesForApp_ConsumesDomainPagesAndStorageFormatIdDirectly()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        Guid instanceGuid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        string uppercaseStorageId = instanceGuid.ToString().ToUpperInvariant();
        const int storageAccountNumber = 7;
        InstanceInternal instance = new()
        {
            Id = uppercaseStorageId,
            AppId = "ttd/app",
            Org = "ttd",
            InstanceOwner = new() { PartyId = "1337" },
            Data = [],
        };
        Queue<InstanceQueryResult> pages = new([
            new InstanceQueryResult
            {
                Instances = [instance],
                Count = 1,
                ContinuationToken = "next-page",
            },
            new InstanceQueryResult { Instances = [], Count = 0 },
        ]);
        List<InstanceQueryParameters> capturedParameters = [];
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    cancellationTokenSource.Token
                )
            )
            .Callback<InstanceQueryParameters, CancellationToken>(
                (parameters, _) => capturedParameters.Add(parameters)
            )
            .ReturnsAsync(() => pages.Dequeue());
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetBlobVersionsForInstance(instanceGuid, cancellationTokenSource.Token)
            )
            .ReturnsAsync([]);
        instanceRepositoryMock
            .Setup(repository => repository.Delete(instanceGuid, cancellationTokenSource.Token))
            .ReturnsAsync(true);
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
            .Setup(repository =>
                repository.DeleteDataBlobs(
                    instance.Org,
                    instance.AppId,
                    uppercaseStorageId,
                    storageAccountNumber,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForInstance(instanceGuid.ToString(), cancellationTokenSource.Token)
            )
            .ReturnsAsync(true);
        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            Mock.Of<IInstanceEventRepository>(),
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
            NullLogger<CleanupController>.Instance
        );

        ActionResult result = await controller.CleanupInstancesForApp(
            "ttd/app",
            cancellationTokenSource.Token
        );

        Assert.IsType<OkResult>(result);
        Assert.Equal(2, capturedParameters.Count);
        Assert.All(capturedParameters, parameters => Assert.Equal("ttd/app", parameters.AppId));
        Assert.All(capturedParameters, parameters => Assert.Equal(5000, parameters.Size));
        Assert.All(capturedParameters, parameters => Assert.True(parameters.IncludeDataElements));
        Assert.Null(capturedParameters[0].ContinuationToken);
        Assert.Equal("next-page", capturedParameters[1].ContinuationToken);
        Assert.Empty(pages);
        instanceRepositoryMock.VerifyAll();
        applicationRepositoryMock.VerifyAll();
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task CleanupInstancesForApp_CancelledDomainResult_PreservesSuccessfulStatusAndNoSideEffects()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.Is<InstanceQueryParameters>(parameters =>
                        parameters.AppId == "ttd/app"
                        && parameters.Size == 5000
                        && parameters.ContinuationToken == null
                        && parameters.IncludeDataElements
                    ),
                    cancellationTokenSource.Token
                )
            )
            .ReturnsAsync(
                new InstanceQueryResult
                {
                    Instances = [],
                    Count = 0,
                    Exception = "The query was canceled.",
                }
            );
        CleanupController controller = new(
            instanceRepositoryMock.Object,
            Mock.Of<IApplicationRepository>(),
            Mock.Of<IBlobRepository>(),
            Mock.Of<IDataRepository>(),
            Mock.Of<IInstanceEventRepository>(),
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
            NullLogger<CleanupController>.Instance
        );

        ActionResult result = await controller.CleanupInstancesForApp(
            "ttd/app",
            cancellationTokenSource.Token
        );

        Assert.IsType<OkResult>(result);
        instanceRepositoryMock.VerifyAll();
        instanceRepositoryMock.Verify(
            repository => repository.Delete(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDataelements_VersionedElement_TreatsDeletedOrMissingVersionedBlobsAsSuccessBeforeMetadata()
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
            dataElement.FromApiModel(secondBlobVersionId),
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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 0L));
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
                repository.DeleteBlobsIfExists(
                    "storage-org",
                    It.Is<IReadOnlyList<string>>(paths =>
                        paths.SequenceEqual(expectedBlobStoragePaths)
                    ),
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(0, callOrder++))
            .ReturnsAsync([true, true]);
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
                repository.DeleteForCleanup(
                    deletedDataElement.DataElement,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(2, callOrder++))
            .ReturnsAsync(true);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
            NullLogger<CleanupController>.Instance
        );

        // Act
        ActionResult result = await controller.CleanupDataelements(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        Assert.Equal(3, callOrder);
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
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
        DeletedDataElementInternal deletedDataElement = new(dataElement.FromApiModel(null), []);

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
            .ReturnsAsync((InstanceInternal)null);
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
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
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
            repository =>
                repository.DeleteForCleanup(
                    deletedDataElement.DataElement,
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupDataelements_OrphanBlobVersions_DeletesOnlySuccessfulVersionedBlobsFromMetadata()
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
                repository.DeleteBlobsIfExists(
                    "storage-org",
                    It.Is<IReadOnlyList<string>>(paths =>
                        paths.SequenceEqual(expectedBlobStoragePaths)
                    ),
                    storageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(0, callOrder++))
            .ReturnsAsync([true, false]);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteOrphanBlobVersions(
                    It.Is<IReadOnlyList<string>>(versions =>
                        versions.SequenceEqual(new[] { firstBlobVersionId })
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(1, callOrder++))
            .ReturnsAsync(1);

        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            instanceEventRepositoryMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            Options.Create(new StorageCleanupSettings()),
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
                repository.DeleteForCleanup(
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupInstanceMutationIdempotency_UsesMinimumRetention()
    {
        // Arrange
        Mock<IInstanceMutationRepository> instanceMutationRepositoryMock = new();
        DateTime capturedCutoff = default;
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.DeleteIdempotencyRecordsCreatedBefore(
                    It.IsAny<DateTime>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<DateTime, int, CancellationToken>((cutoff, _, _) => capturedCutoff = cutoff)
            .ReturnsAsync(3);

        CleanupController controller = new(
            Mock.Of<IInstanceRepository>(),
            Mock.Of<IApplicationRepository>(),
            Mock.Of<IBlobRepository>(),
            Mock.Of<IDataRepository>(),
            Mock.Of<IInstanceEventRepository>(),
            instanceMutationRepositoryMock.Object,
            Options.Create(
                new StorageCleanupSettings { InstanceMutationIdempotencyRetentionHours = 1 }
            ),
            NullLogger<CleanupController>.Instance
        );
        DateTime before = DateTime.UtcNow;

        // Act
        ActionResult result = await controller.CleanupInstanceMutationIdempotency(
            CancellationToken.None
        );
        DateTime after = DateTime.UtcNow;

        // Assert
        Assert.IsType<OkResult>(result);
        Assert.InRange(
            capturedCutoff,
            before
                - TimeSpan.FromHours(
                    StorageCleanupSettings.MinimumInstanceMutationIdempotencyRetentionHours
                ),
            after
                - TimeSpan.FromHours(
                    StorageCleanupSettings.MinimumInstanceMutationIdempotencyRetentionHours
                )
        );
        instanceMutationRepositoryMock.VerifyAll();
    }
}
