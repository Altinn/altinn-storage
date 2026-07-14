#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using static Altinn.Platform.Storage.Interface.Models.SignRequest;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class SigningServiceTest
{
    public static TheoryData<Signee> SigneeData =>
        new(
            new Signee()
            {
                UserId = "1337",
                PersonNumber = "22117612345",
                SystemUserId = null,
                OrganisationNumber = null,
            },
            new Signee()
            {
                UserId = string.Empty,
                PersonNumber = null,
                SystemUserId = null,
                OrganisationNumber = "524446332",
            },
            new Signee()
            {
                UserId = string.Empty,
                PersonNumber = null,
                SystemUserId = Guid.NewGuid(),
                OrganisationNumber = "524446332",
            }
        );

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched(Signee signee)
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal()
                {
                    Id = Guid.NewGuid().ToString(),
                    InstanceOwner = new(),
                    Versions = new StorageVersions(7, 11),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" },
                    },
                }
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));

        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.Is<DataElementCreateOptions>(options => options.Locked),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new StagedDataElementBlob(
                    new DataElementInternal { Id = Guid.NewGuid().ToString(), Locked = true },
                    DateTimeOffset.Now
                )
            );

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    0,
                    It.Is<InstanceMutationCommit>(mutation =>
                        mutation.ExpectedInstanceVersion == 7
                        && mutation.ExpectedProcessStateVersion == 11
                        && mutation.CreateDataElements.Count == 1
                        && mutation.DeleteDataElements.Count == 0
                        && mutation.InstanceEvents.Count == 1
                        && mutation.InstanceEvents[0].EventType
                            == InstanceEventType.Signed.ToString()
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures = new List<DataElementSignature>
            {
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            },
            Signee = signee,
        };

        // Act
        var performedBy = !string.IsNullOrWhiteSpace(signee.UserId)
            ? signee.UserId
            : signee.OrganisationNumber;
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(
            Guid.NewGuid(),
            signRequest,
            performedBy,
            It.IsAny<CancellationToken>(),
            7,
            11
        );

        // Assert
        Assert.True(created);
        Assert.Null(serviceError);
        instanceRepositoryMock.VerifyAll();
        instanceRepositoryMock.Verify(
            rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()),
            Times.Once
        );
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
        instanceEventServiceMock.VerifyAll();
        instanceMutationRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task CreateSignDocument_SigningSuccessful_ResultVersionsComeFromApplySnapshot()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal()
                {
                    Id = Guid.NewGuid().ToString(),
                    InstanceOwner = new(),
                    Versions = new StorageVersions(7, 11),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" },
                    },
                }
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));

        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new StagedDataElementBlob(
                    new DataElementInternal { Id = Guid.NewGuid().ToString(), Locked = true },
                    DateTimeOffset.Now
                )
            );

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(It.IsAny<InstanceEventType>(), It.IsAny<InstanceInternal>())
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(
                    false,
                    [],
                    new InstanceInternal
                    {
                        Id = Guid.NewGuid().ToString(),
                        Versions = new StorageVersions(9, 12),
                    }
                )
            );

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            new Mock<IBlobRepository>().Object,
            new Mock<ILogger<SigningService>>().Object
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures = new List<DataElementSignature>
            {
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            },
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        // Act
        SignDocumentCreateResult result = await service.CreateSignDocument(
            Guid.NewGuid(),
            signRequest,
            "1337",
            CancellationToken.None,
            7,
            11
        );

        // Assert
        Assert.True(result.Created);
        Assert.Equal(new StorageVersions(9, 12), result.Versions);
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_OldSignatureDeletedEventIsCommittedWithSignedEvent(
        Signee signee
    )
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var signatureDataType = "sign-data-type";
        string expectedBlobStoragePath = "org/app/instance/signature.json";

        SignDocument oldSignDocument = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = default,
            SigneeInfo = signee,
            DataElementSignatures = [],
        };

        DataElementInternal oldSignatureDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = signatureDataType,
            BlobStoragePath = expectedBlobStoragePath,
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        var instance = new InstanceInternal()
        {
            Id = instanceGuid.ToString(),
            AppId = "org/app",
            Org = "org",
            InstanceOwner = new InstanceOwner(),
            Versions = new StorageVersions(7, 11),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "signing",
                },
            },
            Data = [oldSignatureDataElement],
        };

        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));

        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.Is<DataElementCreateOptions>(options => options.Locked),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new StagedDataElementBlob(
                    new DataElementInternal { Id = Guid.NewGuid().ToString(), Locked = true },
                    DateTimeOffset.Now
                )
            );

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Deleted),
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.Id == oldSignatureDataElement.Id
                    )
                )
            )
            .Returns(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = oldSignatureDataElement.Id,
                }
            );

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        InstanceMutationCommit capturedMutation = null;
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    0,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application { StorageAccountNumber = null });

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock
            .Setup(x =>
                x.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(oldSignDocument)));

        dataServiceMock
            .Setup(dsm =>
                dsm.CleanupDeletedDataElementBlobs(
                    It.Is<InstanceInternal>(targetInstance =>
                        targetInstance.Id == instanceGuid.ToString()
                    ),
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.Id == oldSignatureDataElement.Id
                    ),
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object
        );

        // Act
        var signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = signee,
        };

        string performedBy = !string.IsNullOrWhiteSpace(signee.UserId)
            ? signee.UserId
            : signee.OrganisationNumber;
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(
            instanceGuid,
            signRequest,
            performedBy,
            It.IsAny<CancellationToken>(),
            7,
            11
        );

        // Assert
        Assert.True(created);
        Assert.Null(serviceError);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
        instanceMutationRepositoryMock.VerifyAll();
        instanceEventServiceMock.Verify(esm =>
            esm.BuildInstanceEvent(
                It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                It.IsAny<InstanceInternal>()
            )
        );
        Assert.NotNull(capturedMutation);
        Assert.Same(instance, capturedMutation.InstanceUpdates);
        Assert.Single(capturedMutation.CreateDataElements);
        Assert.Single(capturedMutation.DeleteDataElements);
        Assert.Equal(
            oldSignatureDataElement.Id,
            capturedMutation.DeleteDataElements[0].DataElement.Id
        );
        Assert.True(capturedMutation.DeleteDataElements[0].IgnoreLock);
        Assert.Equal(7, capturedMutation.ExpectedInstanceVersion);
        Assert.Equal(11, capturedMutation.ExpectedProcessStateVersion);
        Assert.Equal(2, capturedMutation.InstanceEvents.Count);
        Assert.Equal(
            InstanceEventType.Signed.ToString(),
            capturedMutation.InstanceEvents[0].EventType
        );
        Assert.Equal(
            InstanceEventType.Deleted.ToString(),
            capturedMutation.InstanceEvents[1].EventType
        );
        Assert.Equal(oldSignatureDataElement.Id, capturedMutation.InstanceEvents[1].DataId);
        instanceEventServiceMock.Verify(
            esm =>
                esm.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    null,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        blobRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task CreateSignDocument_SigningFailed_InstanceNotExists()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceInternal)null);

        var applicationServiceMock = new Mock<IApplicationService>();
        var dataServiceMock = new Mock<IDataService>();
        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object
        );

        // Act
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(
            Guid.NewGuid(),
            new SignRequest(),
            "1337",
            It.IsAny<CancellationToken>()
        );

        // Assert
        Assert.False(created);
        Assert.Equal(404, serviceError.ErrorCode);
        instanceRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task CreateSignDocument_SigningFailed_InvalidDatatype()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal()
                {
                    Versions = new StorageVersions(1, 1),
                    InstanceOwner = new(),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" },
                    },
                }
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((false, new ServiceError(404, $"Cannot find application in storage")));

        var dataServiceMock = new Mock<IDataService>();
        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock
            .Setup(x =>
                x.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream("whatever"u8.ToArray()));

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object
        );

        // Act
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(
            Guid.NewGuid(),
            new SignRequest(),
            "1337",
            It.IsAny<CancellationToken>()
        );

        // Assert
        Assert.False(created);
        Assert.Equal(404, serviceError.ErrorCode);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningFailed_DataElementNotExists(Signee signee)
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal()
                {
                    Versions = new StorageVersions(1, 1),
                    InstanceOwner = new(),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" },
                    },
                }
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((null, new ServiceError(404, "DataElement not found")));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock
            .Setup(x =>
                x.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream("whatever"u8.ToArray()));

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures = new List<DataElementSignature>
            {
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            },
            Signee = signee,
        };

        // Act
        var performedBy = !string.IsNullOrWhiteSpace(signee.UserId)
            ? signee.UserId
            : signee.OrganisationNumber;
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(
            Guid.NewGuid(),
            signRequest,
            performedBy,
            It.IsAny<CancellationToken>()
        );

        // Assert
        Assert.False(created);
        Assert.Equal(404, serviceError.ErrorCode);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
    }

    [Fact]
    public async Task CreateSignDocument_UploadThrows_PropagatesException()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal
                {
                    Versions = new StorageVersions(1, 1),
                    InstanceOwner = new(),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" },
                    },
                }
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));
        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            Mock.Of<IInstanceMutationRepository>(),
            applicationRepositoryMock.Object,
            Mock.Of<IBlobRepository>(),
            Mock.Of<ILogger<SigningService>>()
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        // Act/assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateSignDocument(Guid.NewGuid(), signRequest, "1337", CancellationToken.None)
        );

        Assert.Equal("metadata create failed", exception.Message);
        instanceEventServiceMock.Verify(
            service =>
                service.DispatchEvent(It.IsAny<InstanceEventType>(), It.IsAny<InstanceInternal>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_AggregateMutationThrows_CleansStagedBlobAndPropagatesException()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var signatureDataType = "sign-data-type";
        var signee = new Signee { UserId = "1337", PersonNumber = "22117612345" };

        SignDocument oldSignDocument = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = default,
            SigneeInfo = signee,
            DataElementSignatures = [],
        };
        DataElementInternal oldSignatureDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            DataType = signatureDataType,
            BlobStoragePath = "org/app/instance/signature.json",
        };
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "org/app",
            Org = "org",
            InstanceOwner = new InstanceOwner(),
            Versions = new StorageVersions(1, 1),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "signing",
                },
            },
            Data = [oldSignatureDataElement],
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));
        DataElementInternal stagedDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "org/app/instance/data-elements/staged",
            BlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7()),
        };
        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new StagedDataElementBlob(stagedDataElement, DateTimeOffset.Now));
        dataServiceMock
            .Setup(dsm =>
                dsm.DeleteStagedDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    stagedDataElement,
                    It.IsAny<int?>()
                )
            )
            .Returns(Task.CompletedTask);

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Deleted),
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.Id == oldSignatureDataElement.Id
                    )
                )
            )
            .Returns(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = oldSignatureDataElement.Id,
                }
            );

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    0,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new RepositoryException("metadata mutation failed"));
        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock
            .Setup(repository =>
                repository.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(oldSignDocument)));

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            Mock.Of<ILogger<SigningService>>()
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = signatureDataType,
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = signee,
        };

        // Act/assert
        var exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            service.CreateSignDocument(instanceGuid, signRequest, "1337", CancellationToken.None)
        );

        Assert.Equal("metadata mutation failed", exception.Message);
        instanceEventServiceMock.Verify(
            eventService =>
                eventService.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>()
                ),
            Times.Never
        );
        dataServiceMock.Verify(
            dsm =>
                dsm.DeleteStagedDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataServiceMock.Verify(
            dsm =>
                dsm.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_AggregateMutationOutcomeUnknown_LeavesStagedBlobForOrphanCleanup()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var signee = new Signee { UserId = "1337", PersonNumber = "22117612345" };
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "org/app",
            Org = "org",
            InstanceOwner = new InstanceOwner(),
            Versions = new StorageVersions(1, 1),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "signing",
                },
            },
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));
        DataElementInternal stagedDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "org/app/instance/data-elements/staged",
            BlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7()),
        };
        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new StagedDataElementBlob(stagedDataElement, DateTimeOffset.Now));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    0,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new TimeoutException("commit outcome unknown"));
        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            Mock.Of<ILogger<SigningService>>()
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = signee,
        };

        // Act/assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            service.CreateSignDocument(instanceGuid, signRequest, "1337", CancellationToken.None)
        );

        Assert.Equal("commit outcome unknown", exception.Message);
        dataServiceMock.Verify(
            dsm =>
                dsm.DeleteStagedDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_StalePrecondition_CleansStagedBlobAndDoesNotDispatchEventsOrDeleteOldBlobs()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var signatureDataType = "sign-data-type";
        var signee = new Signee { UserId = "1337", PersonNumber = "22117612345" };

        SignDocument oldSignDocument = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = default,
            SigneeInfo = signee,
            DataElementSignatures = [],
        };
        DataElementInternal oldSignatureDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = signatureDataType,
            BlobStoragePath = "org/app/instance/signature.json",
        };
        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "org/app",
            Org = "org",
            InstanceOwner = new InstanceOwner(),
            Versions = new StorageVersions(7, 11),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "signing",
                },
            },
            Data = [oldSignatureDataElement],
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(rm => rm.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(asm =>
                asm.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock
            .Setup(am =>
                am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application());

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock
            .Setup(dsm =>
                dsm.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((Guid.NewGuid().ToString(), null));
        DataElementInternal stagedDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "org/app/instance/data-elements/staged",
            BlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7()),
        };
        dataServiceMock
            .Setup(dsm =>
                dsm.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new StagedDataElementBlob(stagedDataElement, DateTimeOffset.Now));
        dataServiceMock
            .Setup(dsm =>
                dsm.DeleteStagedDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    stagedDataElement,
                    It.IsAny<int?>()
                )
            )
            .Returns(Task.CompletedTask);

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });
        instanceEventServiceMock
            .Setup(esm =>
                esm.BuildInstanceEvent(
                    It.Is<InstanceEventType>(ies => ies == InstanceEventType.Deleted),
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.Id == oldSignatureDataElement.Id
                    )
                )
            )
            .Returns(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = oldSignatureDataElement.Id,
                }
            );

        var instanceMutationRepositoryMock = new Mock<IInstanceMutationRepository>();
        instanceMutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    0,
                    It.Is<InstanceMutationCommit>(mutation =>
                        mutation.DeleteDataElements.Count == 1
                        && mutation.DeleteDataElements[0].DataElement.Id
                            == oldSignatureDataElement.Id
                        && mutation.DeleteDataElements[0].IgnoreLock
                        && mutation.InstanceEvents.Count == 2
                        && mutation.InstanceEvents.Any(instanceEvent =>
                            instanceEvent.EventType == InstanceEventType.Signed.ToString()
                        )
                        && mutation.InstanceEvents.Any(instanceEvent =>
                            instanceEvent.EventType == InstanceEventType.Deleted.ToString()
                            && instanceEvent.DataId == oldSignatureDataElement.Id
                        )
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InstanceVersionMismatchException(9, 11));

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock
            .Setup(x =>
                x.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(oldSignDocument)));

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            new Mock<ILogger<SigningService>>().Object
        );

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = signatureDataType,
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = signee,
        };

        // Act
        SignDocumentCreateResult result = await service.CreateSignDocument(
            instanceGuid,
            signRequest,
            "1337",
            CancellationToken.None,
            7,
            11
        );

        // Assert
        Assert.False(result.Created);
        Assert.Equal(412, result.ServiceError.ErrorCode);
        Assert.Equal("instance_version_mismatch", result.ServiceError.ErrorMessage);
        Assert.Equal(9, result.Versions.InstanceVersion);
        Assert.Equal(11, result.Versions.ProcessStateVersion);
        instanceEventServiceMock.Verify(
            esm =>
                esm.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                ),
            Times.Never
        );
        dataServiceMock.Verify(
            dsm =>
                dsm.DeleteStagedDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    stagedDataElement,
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataServiceMock.Verify(
            dsm =>
                dsm.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }
}
