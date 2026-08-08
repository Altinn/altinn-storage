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
using Microsoft.Extensions.Logging;
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
        InstanceInternal instance = new()
        {
            Id = instanceGuid,
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
                    instanceGuid,
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForInstance(instanceGuid, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(true);

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
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
            Id = instanceGuid,
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
                    instanceGuid,
                    blobStorageAccountNumber,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false);

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
        );

        // Act
        ActionResult result = await controller.CleanupInstances(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        blobRepositoryMock.VerifyAll();
        instanceRepositoryMock.VerifyAll();
        dataRepositoryMock.Verify(
            repository =>
                repository.DeleteForInstance(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        instanceRepositoryMock.Verify(
            repository => repository.Delete(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupInstancesForApp_PagesThroughDomainQueryResultsUntilExhausted()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        Guid instanceGuid = Guid.NewGuid();
        const int storageAccountNumber = 7;
        InstanceInternal instance = new()
        {
            Id = instanceGuid,
            AppId = "ttd/app",
            Org = "ttd",
            InstanceOwner = new() { PartyId = "1337" },
            Data = [],
        };
        Queue<InstanceQueryResult> pages = new([
            new InstanceQueryResult { Instances = [instance], ContinuationToken = "next-page" },
            new InstanceQueryResult { Instances = [] },
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
                    instanceGuid,
                    storageAccountNumber,
                    CancellationToken.None
                )
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForInstance(instanceGuid, cancellationTokenSource.Token)
            )
            .ReturnsAsync(true);
        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
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
                new InstanceQueryResult { Instances = [], Exception = "The query was canceled." }
            );
        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock
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
                instanceGuid,
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
        string[] expectedBlobStoragePaths =
        [
            BlobRepository.GetVersionedBlobPath("stored/app", instanceGuid, firstBlobVersionId),
            BlobRepository.GetVersionedBlobPath("stored/app", instanceGuid, secondBlobVersionId),
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

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
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

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
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
            BlobRepository.GetVersionedBlobPath("ttd/app", instanceGuid, firstBlobVersionId),
            BlobRepository.GetVersionedBlobPath("ttd/app", instanceGuid, secondBlobVersionId),
        ];
        int callOrder = 0;

        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();

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

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock
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
    public async Task CleanupDataelements_DeleteForCleanupReturnsFalse_DoesNotIncrementSuccessfulCount()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid firstDataElementId = Guid.NewGuid();
        Guid secondDataElementId = Guid.NewGuid();
        DataElementInternal firstDataElement = new DataElement
        {
            Id = firstDataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = $"ttd/app/{instanceGuid}/data/{firstDataElementId}",
        }.FromApiModel(null);
        DataElementInternal secondDataElement = new DataElement
        {
            Id = secondDataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobStoragePath = $"ttd/app/{instanceGuid}/data/{secondDataElementId}",
        }.FromApiModel(null);
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
        Mock<ILogger<CleanupController>> loggerMock = new();

        instanceRepositoryMock
            .Setup(repository =>
                repository.GetHardDeletedDataElements(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([
                new DeletedDataElementInternal(firstDataElement, []),
                new DeletedDataElementInternal(secondDataElement, []),
            ]);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 0L));
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOrphanBlobVersionsForCleanup(It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);
        applicationRepositoryMock
            .Setup(repository =>
                repository.FindOne(instance.AppId, instance.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application { Id = instance.AppId, Org = instance.Org });
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlob("ttd", firstDataElement.BlobStoragePath, null)
            )
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlob("ttd", secondDataElement.BlobStoragePath, null)
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForCleanup(firstDataElement, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteForCleanup(secondDataElement, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(false);

        CleanupController controller = CreateController(
            instanceRepositoryMock: instanceRepositoryMock,
            applicationRepositoryMock: applicationRepositoryMock,
            blobRepositoryMock: blobRepositoryMock,
            dataRepositoryMock: dataRepositoryMock,
            loggerMock: loggerMock
        );

        // Act
        ActionResult result = await controller.CleanupDataelements(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
        instanceRepositoryMock.VerifyAll();
        applicationRepositoryMock.VerifyAll();
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
        loggerMock.Verify(
            logger =>
                logger.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (state, _) =>
                            state
                                .ToString()
                                .Contains(
                                    $"Data element not found for dataElement Id: {secondDataElementId}"
                                )
                    ),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()
                ),
            Times.Once
        );
        loggerMock.Verify(
            logger =>
                logger.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (state, _) =>
                            state
                                .ToString()
                                .Contains("1 of 2 data elements and 0 orphan blob versions deleted")
                    ),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()
                ),
            Times.Once
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

        CleanupController controller = CreateController(
            instanceMutationRepositoryMock: instanceMutationRepositoryMock,
            cleanupSettings: new StorageCleanupSettings
            {
                InstanceMutationIdempotencyRetentionHours = 1,
            }
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

    private static CleanupController CreateController(
        Mock<IInstanceRepository> instanceRepositoryMock = null,
        Mock<IApplicationRepository> applicationRepositoryMock = null,
        Mock<IBlobRepository> blobRepositoryMock = null,
        Mock<IDataRepository> dataRepositoryMock = null,
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = null,
        Mock<IInstanceMutationRepository> instanceMutationRepositoryMock = null,
        StorageCleanupSettings cleanupSettings = null,
        Mock<ILogger<CleanupController>> loggerMock = null
    ) =>
        new(
            (instanceRepositoryMock ?? new Mock<IInstanceRepository>(MockBehavior.Strict)).Object,
            (
                applicationRepositoryMock ?? new Mock<IApplicationRepository>(MockBehavior.Strict)
            ).Object,
            (blobRepositoryMock ?? new Mock<IBlobRepository>(MockBehavior.Strict)).Object,
            (dataRepositoryMock ?? new Mock<IDataRepository>(MockBehavior.Strict)).Object,
            (
                instanceEventRepositoryMock
                ?? new Mock<IInstanceEventRepository>(MockBehavior.Strict)
            ).Object,
            (
                instanceMutationRepositoryMock
                ?? new Mock<IInstanceMutationRepository>(MockBehavior.Strict)
            ).Object,
            Options.Create(cleanupSettings ?? new StorageCleanupSettings()),
            loggerMock?.Object ?? NullLogger<CleanupController>.Instance
        );
}
