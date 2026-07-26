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
    private const string _signatureDataType = "sign-data-type";

    public static TheoryData<Signee> SigneeData =>
        new(
            new Signee { UserId = "1337", PersonNumber = "22117612345" },
            new Signee { UserId = string.Empty, OrganisationNumber = "524446332" },
            new Signee
            {
                UserId = string.Empty,
                SystemUserId = Guid.NewGuid(),
                OrganisationNumber = "524446332",
            }
        );

    public static TheoryData<StorageVersionMismatchException> ApplyVersionMismatchData =>
        new(
            new InstanceVersionMismatchException(9, 11),
            new ProcessStateVersionMismatchException(9, 11)
        );

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched(Signee signee)
    {
        SigningFixture fixture = CreateSigningFixture(signee);

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            GetPerformedBy(signee),
            CancellationToken.None,
            7,
            11
        );

        Assert.True(result.Created);
        Assert.Null(result.ServiceError);
        Assert.Equal(new StorageVersions(7, 11), result.Versions);
        Assert.NotNull(fixture.CapturedMutation);
        Assert.Single(fixture.CapturedMutation.CreateDataElements);
        Assert.Empty(fixture.CapturedMutation.DeleteDataElements);
        Assert.Equal(7, fixture.CapturedMutation.ExpectedInstanceVersion);
        Assert.Equal(11, fixture.CapturedMutation.ExpectedProcessStateVersion);
        InstanceEvent signedEvent = Assert.Single(fixture.CapturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Signed.ToString(), signedEvent.EventType);
        fixture.InstanceRepository.Verify(
            repository =>
                repository.GetOne(fixture.InstanceGuid, true, It.IsAny<CancellationToken>()),
            Times.Once
        );
        fixture.DataService.Verify(
            service =>
                service.StageDataElementBlob(
                    fixture.Instance,
                    It.IsAny<Stream>(),
                    It.Is<DataElementCreateOptions>(options => options.Locked),
                    7,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_MutationCarriesSignedTimeAndPerformedBy(
        Signee signee
    )
    {
        SigningFixture fixture = CreateSigningFixture(signee);
        string performedBy = GetPerformedBy(signee);

        await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            performedBy,
            CancellationToken.None,
            7,
            11
        );

        Assert.NotNull(fixture.CapturedCreateOptions);
        Assert.NotNull(fixture.CapturedMutation);
        Assert.Equal(performedBy, fixture.CapturedMutation.LastChangedBy);
        Assert.Equal(fixture.CapturedCreateOptions.Created, fixture.CapturedMutation.LastChanged);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 11)]
    [InlineData(7, null)]
    [InlineData(7, 11)]
    public async Task CreateSignDocument_ApplyUsesSnapshotProcessStateVersionAndPreservesClientInstanceVersion(
        int? expectedInstanceVersion,
        int? expectedProcessStateVersion
    )
    {
        SigningFixture fixture = CreateSigningFixture();

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            "1337",
            CancellationToken.None,
            expectedInstanceVersion,
            expectedProcessStateVersion
        );

        Assert.True(result.Created);
        Assert.NotNull(fixture.CapturedMutation);
        Assert.Equal(expectedInstanceVersion, fixture.CapturedMutation.ExpectedInstanceVersion);
        Assert.Equal(11, fixture.CapturedMutation.ExpectedProcessStateVersion);
    }

    [Fact]
    public async Task CreateSignDocument_SigningSuccessful_ResultVersionsComeFromApplySnapshot()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.Instance.InternalId,
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
                        Id = fixture.InstanceGuid.ToString(),
                        Versions = new StorageVersions(9, 12),
                    }
                )
            );

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            "1337",
            CancellationToken.None,
            7,
            11
        );

        Assert.True(result.Created);
        Assert.Equal(new StorageVersions(9, 12), result.Versions);
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_OldSignatureDeletedEventIsCommittedWithSignedEvent(
        Signee signee
    )
    {
        SigningFixture fixture = CreateSigningFixture(signee, existingSignatureCount: 1);

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            GetPerformedBy(signee),
            CancellationToken.None,
            7,
            11
        );

        Assert.True(result.Created);
        Assert.Null(result.ServiceError);
        Assert.NotNull(fixture.CapturedMutation);
        Assert.Same(fixture.Instance, fixture.CapturedMutation.InstanceUpdates);
        Assert.Single(fixture.CapturedMutation.CreateDataElements);
        InstanceMutationDataElementDelete deletedElement = Assert.Single(
            fixture.CapturedMutation.DeleteDataElements
        );
        Assert.Same(fixture.OldSignatureDataElements[0], deletedElement.DataElement);
        Assert.True(deletedElement.IgnoreLock);
        Assert.Equal(
            [InstanceEventType.Signed.ToString(), InstanceEventType.Deleted.ToString()],
            fixture.CapturedMutation.InstanceEvents.Select(instanceEvent => instanceEvent.EventType)
        );
        Assert.Equal(
            fixture.OldSignatureDataElements[0].Id,
            fixture.CapturedMutation.InstanceEvents[1].DataId
        );
        fixture.DataService.Verify(
            service =>
                service.CleanupDeletedDataElementBlobs(
                    fixture.Instance,
                    fixture.OldSignatureDataElements[0],
                    7,
                    CancellationToken.None
                ),
            Times.Once
        );
        fixture.InstanceEventService.Verify(
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
    public async Task CreateSignDocument_SigneeHasDuplicateSignatures_DeletesAllOfThem()
    {
        SigningFixture fixture = CreateSigningFixture(existingSignatureCount: 3);

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            "1337",
            CancellationToken.None,
            7,
            11
        );

        Assert.True(result.Created);
        Assert.NotNull(fixture.CapturedMutation);
        Assert.Single(fixture.CapturedMutation.CreateDataElements);
        Assert.Equal(
            fixture.OldSignatureDataElements,
            fixture.CapturedMutation.DeleteDataElements.Select(delete => delete.DataElement)
        );
        Assert.All(
            fixture.CapturedMutation.DeleteDataElements,
            delete => Assert.True(delete.IgnoreLock)
        );
        Assert.Equal(
            [
                InstanceEventType.Signed.ToString(),
                InstanceEventType.Deleted.ToString(),
                InstanceEventType.Deleted.ToString(),
                InstanceEventType.Deleted.ToString(),
            ],
            fixture.CapturedMutation.InstanceEvents.Select(instanceEvent => instanceEvent.EventType)
        );
        foreach (DataElementInternal oldSignatureDataElement in fixture.OldSignatureDataElements)
        {
            fixture.DataService.Verify(
                service =>
                    service.CleanupDeletedDataElementBlobs(
                        fixture.Instance,
                        oldSignatureDataElement,
                        7,
                        CancellationToken.None
                    ),
                Times.Once
            );
        }
    }

    [Fact]
    public async Task CreateSignDocument_SigningFailed_InstanceNotExists()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture
            .InstanceRepository.Setup(repository =>
                repository.GetOne(fixture.InstanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((InstanceInternal)null);

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            "1337",
            CancellationToken.None,
            null,
            null
        );

        Assert.False(result.Created);
        Assert.Equal(404, result.ServiceError.ErrorCode);
        fixture.ApplicationRepository.Verify(
            repository =>
                repository.FindOne(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateSignDocument_StalePrecheck_ThrowsVersionMismatchBeforeStaging(
        bool instanceVersionMismatch
    )
    {
        SigningFixture fixture = CreateSigningFixture();

        StorageVersionMismatchException exception =
            await Assert.ThrowsAnyAsync<StorageVersionMismatchException>(() =>
                fixture.Sut.CreateSignDocument(
                    fixture.InstanceGuid,
                    fixture.SignRequest,
                    "1337",
                    CancellationToken.None,
                    instanceVersionMismatch ? 6 : 7,
                    instanceVersionMismatch ? 11 : 10
                )
            );

        if (instanceVersionMismatch)
        {
            Assert.IsType<InstanceVersionMismatchException>(exception);
        }
        else
        {
            Assert.IsType<ProcessStateVersionMismatchException>(exception);
        }

        Assert.Equal(7, exception.CurrentInstanceVersion);
        Assert.Equal(11, exception.CurrentProcessStateVersion);
        fixture.DataService.Verify(
            service =>
                service.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_ProcessingInstance_ReturnsConflictBeforeStagingOrApply()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture.Instance.Process.Status = ProcessStatus.Processing;

        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                fixture.Sut.CreateSignDocument(
                    fixture.InstanceGuid,
                    fixture.SignRequest,
                    "1337",
                    CancellationToken.None,
                    7,
                    11
                )
            );

        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        fixture.DataService.Verify(
            service =>
                service.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        fixture.MutationRepository.Verify(
            repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_SigningFailed_InvalidDatatype()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture
            .ApplicationService.Setup(service =>
                service.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((false, new ServiceError(404, "Cannot find application in storage")));

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            "1337",
            CancellationToken.None,
            null,
            null
        );

        Assert.False(result.Created);
        Assert.Equal(404, result.ServiceError.ErrorCode);
        fixture.DataService.Verify(
            service =>
                service.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningFailed_DataElementNotExists(Signee signee)
    {
        SigningFixture fixture = CreateSigningFixture(signee);
        fixture
            .DataService.Setup(service =>
                service.GenerateSha256Hash(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((null, new ServiceError(404, "DataElement not found")));

        SignDocumentCreateResult result = await fixture.Sut.CreateSignDocument(
            fixture.InstanceGuid,
            fixture.SignRequest,
            GetPerformedBy(signee),
            CancellationToken.None,
            null,
            null
        );

        Assert.False(result.Created);
        Assert.Equal(404, result.ServiceError.ErrorCode);
        fixture.DataService.Verify(
            service =>
                service.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_UploadThrows_PropagatesException()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture
            .DataService.Setup(service =>
                service.StageDataElementBlob(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("metadata create failed"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                fixture.Sut.CreateSignDocument(
                    fixture.InstanceGuid,
                    fixture.SignRequest,
                    "1337",
                    CancellationToken.None,
                    null,
                    null
                )
        );

        Assert.Equal("metadata create failed", exception.Message);
        fixture.MutationRepository.Verify(
            repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateSignDocument_AggregateMutationThrows_CleansStagedBlobAndPropagatesException()
    {
        SigningFixture fixture = CreateSigningFixture(existingSignatureCount: 1);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.Instance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new RepositoryException("metadata mutation failed"));

        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            fixture.Sut.CreateSignDocument(
                fixture.InstanceGuid,
                fixture.SignRequest,
                "1337",
                CancellationToken.None,
                null,
                null
            )
        );

        Assert.Equal("metadata mutation failed", exception.Message);
        VerifyStagedBlobDeleted(fixture, Times.Once());
        VerifyOldBlobCleanupNeverRuns(fixture);
    }

    [Fact]
    public async Task CreateSignDocument_AggregateMutationOutcomeUnknown_LeavesStagedBlobForOrphanCleanup()
    {
        SigningFixture fixture = CreateSigningFixture();
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.Instance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new TimeoutException("commit outcome unknown"));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            fixture.Sut.CreateSignDocument(
                fixture.InstanceGuid,
                fixture.SignRequest,
                "1337",
                CancellationToken.None,
                null,
                null
            )
        );

        Assert.Equal("commit outcome unknown", exception.Message);
        VerifyStagedBlobDeleted(fixture, Times.Never());
    }

    [Theory]
    [MemberData(nameof(ApplyVersionMismatchData))]
    public async Task CreateSignDocument_StalePrecondition_CleansStagedBlobAndRethrowsWithoutDispatchOrOldBlobCleanup(
        StorageVersionMismatchException applyException
    )
    {
        SigningFixture fixture = CreateSigningFixture(existingSignatureCount: 1);
        int order = 0;
        int stageOrder = 0;
        int applyOrder = 0;
        int stagedBlobDeleteOrder = 0;
        fixture
            .DataService.Setup(service =>
                service.StageDataElementBlob(
                    fixture.Instance,
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => stageOrder = ++order)
            .ReturnsAsync(
                new StagedDataElementBlob(fixture.StagedDataElement, DateTimeOffset.UtcNow)
            );
        fixture
            .DataService.Setup(service =>
                service.DeleteStagedDataElementBlob(fixture.Instance, fixture.StagedDataElement, 7)
            )
            .Callback(() => stagedBlobDeleteOrder = ++order)
            .Returns(Task.CompletedTask);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.Instance.InternalId,
                    It.Is<InstanceMutationCommit>(mutation =>
                        mutation.DeleteDataElements.Count == 1
                        && mutation.DeleteDataElements[0].DataElement.Id
                            == fixture.OldSignatureDataElements[0].Id
                        && mutation.DeleteDataElements[0].IgnoreLock
                        && mutation.InstanceEvents.Count == 2
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => applyOrder = ++order)
            .ThrowsAsync(applyException);

        StorageVersionMismatchException exception =
            await Assert.ThrowsAnyAsync<StorageVersionMismatchException>(() =>
                fixture.Sut.CreateSignDocument(
                    fixture.InstanceGuid,
                    fixture.SignRequest,
                    "1337",
                    CancellationToken.None,
                    7,
                    11
                )
            );

        Assert.IsType(applyException.GetType(), exception);
        Assert.Same(applyException, exception);
        Assert.Equal(9, exception.CurrentInstanceVersion);
        Assert.Equal(11, exception.CurrentProcessStateVersion);
        Assert.True(stageOrder < applyOrder);
        Assert.True(applyOrder < stagedBlobDeleteOrder);
        fixture.DataService.Verify(
            service =>
                service.StageDataElementBlob(
                    fixture.Instance,
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    7,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.MutationRepository.Verify(
            repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.Instance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        VerifyStagedBlobDeleted(fixture, Times.Once());
        VerifyOldBlobCleanupNeverRuns(fixture);
        fixture.InstanceEventService.Verify(
            service =>
                service.DispatchEvent(It.IsAny<InstanceEventType>(), It.IsAny<InstanceInternal>()),
            Times.Never
        );
        fixture.InstanceEventService.Verify(
            service =>
                service.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                ),
            Times.Never
        );
    }

    private static void VerifyStagedBlobDeleted(SigningFixture fixture, Times times) =>
        fixture.DataService.Verify(
            service =>
                service.DeleteStagedDataElementBlob(fixture.Instance, fixture.StagedDataElement, 7),
            times
        );

    private static void VerifyOldBlobCleanupNeverRuns(SigningFixture fixture)
    {
        fixture.DataService.Verify(
            service =>
                service.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    private static string GetPerformedBy(Signee signee) =>
        !string.IsNullOrWhiteSpace(signee.UserId) ? signee.UserId : signee.OrganisationNumber;

    private static SigningFixture CreateSigningFixture(
        Signee signee = null,
        int existingSignatureCount = 0
    )
    {
        signee ??= new Signee { UserId = "1337", PersonNumber = "22117612345" };
        Guid instanceGuid = Guid.NewGuid();
        List<DataElementInternal> oldSignatureDataElements = [];
        List<SignDocument> oldSignDocuments = [];
        for (int index = 0; index < existingSignatureCount; index++)
        {
            oldSignatureDataElements.Add(
                new DataElementInternal
                {
                    Id = Guid.NewGuid().ToString(),
                    InstanceGuid = instanceGuid.ToString(),
                    DataType = _signatureDataType,
                    BlobStoragePath = $"org/app/instance/signature-{index}.json",
                }
            );
            oldSignDocuments.Add(
                new SignDocument
                {
                    Id = Guid.NewGuid().ToString(),
                    InstanceGuid = instanceGuid.ToString(),
                    SigneeInfo = signee,
                    DataElementSignatures = [],
                }
            );
        }

        InstanceInternal instance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = "org/app",
            Org = "org",
            InstanceOwner = new InstanceOwner(),
            LastChangedBy = "previous-writer",
            Versions = new StorageVersions(7, 11),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "signing",
                },
            },
            Data = [.. oldSignatureDataElements],
        };
        DataElementInternal stagedDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = "org/app/instance/data-elements/staged",
            BlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7()),
            Locked = true,
        };
        SignRequest signRequest = new()
        {
            SignatureDocumentDataType = _signatureDataType,
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

        Mock<IInstanceRepository> instanceRepository = new();
        Mock<IDataService> dataService = new();
        Mock<IApplicationService> applicationService = new();
        Mock<IInstanceEventService> instanceEventService = new();
        Mock<IInstanceMutationRepository> mutationRepository = new();
        Mock<IApplicationRepository> applicationRepository = new();
        Mock<IBlobRepository> blobRepository = new();

        instanceRepository
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(instance);
        applicationRepository
            .Setup(repository =>
                repository.FindOne("org/app", "org", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application { StorageAccountNumber = 7 });
        applicationService
            .Setup(service =>
                service.ValidateDataTypeForApp("org", "org/app", _signatureDataType, "Task_1")
            )
            .ReturnsAsync((true, null));
        dataService
            .Setup(service => service.GenerateSha256Hash("org", instanceGuid, It.IsAny<Guid>(), 7))
            .ReturnsAsync((Guid.NewGuid().ToString(), null));
        dataService
            .Setup(service => service.DeleteStagedDataElementBlob(instance, stagedDataElement, 7))
            .Returns(Task.CompletedTask);
        instanceEventService
            .Setup(service => service.BuildInstanceEvent(InstanceEventType.Signed, instance))
            .Returns(new InstanceEvent { EventType = InstanceEventType.Signed.ToString() });

        SigningFixture fixture = new(
            instanceGuid,
            instance,
            oldSignatureDataElements,
            stagedDataElement,
            signRequest,
            instanceRepository,
            dataService,
            applicationService,
            instanceEventService,
            mutationRepository,
            applicationRepository,
            blobRepository
        );

        dataService
            .Setup(service =>
                service.StageDataElementBlob(
                    instance,
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<InstanceInternal, Stream, DataElementCreateOptions, int?, CancellationToken>(
                (_, _, options, _, _) => fixture.CapturedCreateOptions = options
            )
            .ReturnsAsync(new StagedDataElementBlob(stagedDataElement, DateTimeOffset.UtcNow));

        mutationRepository
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    instance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => fixture.CapturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );

        for (int index = 0; index < oldSignatureDataElements.Count; index++)
        {
            DataElementInternal oldSignatureDataElement = oldSignatureDataElements[index];
            byte[] serializedSignDocument = JsonSerializer.SerializeToUtf8Bytes(
                oldSignDocuments[index]
            );
            blobRepository
                .Setup(repository =>
                    repository.ReadBlob(
                        "org",
                        oldSignatureDataElement.BlobStoragePath,
                        7,
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(() => new MemoryStream(serializedSignDocument));
            instanceEventService
                .Setup(service =>
                    service.BuildInstanceEvent(
                        InstanceEventType.Deleted,
                        instance,
                        oldSignatureDataElement
                    )
                )
                .Returns(
                    new InstanceEvent
                    {
                        EventType = InstanceEventType.Deleted.ToString(),
                        DataId = oldSignatureDataElement.Id,
                    }
                );
            dataService
                .Setup(service =>
                    service.CleanupDeletedDataElementBlobs(
                        It.IsAny<InstanceInternal>(),
                        oldSignatureDataElement,
                        7,
                        It.IsAny<CancellationToken>()
                    )
                )
                .Returns(Task.CompletedTask);
        }

        fixture.Sut = new SigningService(
            instanceRepository.Object,
            dataService.Object,
            applicationService.Object,
            instanceEventService.Object,
            mutationRepository.Object,
            applicationRepository.Object,
            blobRepository.Object,
            Mock.Of<ILogger<SigningService>>()
        );
        return fixture;
    }

    private sealed class SigningFixture(
        Guid instanceGuid,
        InstanceInternal instance,
        IReadOnlyList<DataElementInternal> oldSignatureDataElements,
        DataElementInternal stagedDataElement,
        SignRequest signRequest,
        Mock<IInstanceRepository> instanceRepository,
        Mock<IDataService> dataService,
        Mock<IApplicationService> applicationService,
        Mock<IInstanceEventService> instanceEventService,
        Mock<IInstanceMutationRepository> mutationRepository,
        Mock<IApplicationRepository> applicationRepository,
        Mock<IBlobRepository> blobRepository
    )
    {
        public Guid InstanceGuid { get; } = instanceGuid;

        public InstanceInternal Instance { get; } = instance;

        public IReadOnlyList<DataElementInternal> OldSignatureDataElements { get; } =
            oldSignatureDataElements;

        public DataElementInternal StagedDataElement { get; } = stagedDataElement;

        public SignRequest SignRequest { get; } = signRequest;

        public Mock<IInstanceRepository> InstanceRepository { get; } = instanceRepository;

        public Mock<IDataService> DataService { get; } = dataService;

        public Mock<IApplicationService> ApplicationService { get; } = applicationService;

        public Mock<IInstanceEventService> InstanceEventService { get; } = instanceEventService;

        public Mock<IInstanceMutationRepository> MutationRepository { get; } = mutationRepository;

        public Mock<IApplicationRepository> ApplicationRepository { get; } = applicationRepository;

        public Mock<IBlobRepository> BlobRepository { get; } = blobRepository;

        public SigningService Sut { get; set; }

        public InstanceMutationCommit CapturedMutation { get; set; }

        public DataElementCreateOptions CapturedCreateOptions { get; set; }
    }
}
