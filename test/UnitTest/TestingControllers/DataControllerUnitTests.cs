#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class DataControllerUnitTests
{
    public enum DataWriteEndpoint
    {
        Overwrite,
        CreateAndUpload,
    }

    public enum LateFailureDependency
    {
        FileScan,
        EventDispatch,
    }

    private static List<string> _forbiddenUpdateProps =
    [
        "/created",
        "/createdBy",
        "/id",
        "/instanceGuid",
        "/blobStoragePath",
        "/dataType",
    ];
    private static readonly List<string> _expectedPropertiesForOverwrite =
    [
        "/contentType",
        "/filename",
        "/lastChangedBy",
        "/lastChanged",
        "/refs",
        "/size",
        "/fileScanResult",
        "/references",
        "/blobStoragePath",
        "/currentBlobVersion",
    ];
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly int _instanceOwnerPartyId = 1337;
    private readonly string _org = "ttd";
    private readonly string _appId = "ttd/apps-test";
    private readonly string _dataType = "attachment";

    [Fact]
    public async Task Delete_ImmediateDelete_AppliesMetadataDeleteAndDeletedEventBeforeBlobCleanup()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        InstanceMutationCommit capturedMutation = null;
        int order = 0;
        int buildEventOrder = 0;
        int applyOrder = 0;
        int cleanupOrder = 0;

        fixture.OnBuildDeletedEvent = () => buildEventOrder = ++order;
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) =>
                {
                    applyOrder = ++order;
                    capturedMutation = mutation;
                }
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(
                    false,
                    [],
                    new InstanceInternal { Versions = new StorageVersions(8, 6) }
                )
            );
        fixture
            .DataService.Setup(service =>
                service.CleanupDeletedDataElementBlobs(
                    fixture.InstanceInternal,
                    fixture.DataElementInternal,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => cleanupOrder = ++order)
            .Returns(Task.CompletedTask);

        ActionResult<DataElement> result = await fixture.Sut.Delete(
            fixture.InstanceOwnerPartyId,
            fixture.InstanceGuid,
            fixture.DataElementId,
            false,
            CancellationToken.None,
            ifInstanceVersionMatch: "4",
            ifProcessStateVersionMatch: "2"
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        DataElement response = Assert.IsType<DataElement>(ok.Value);
        Assert.NotSame(fixture.DataElement, response);
        Assert.Equal(fixture.DataElement.Id, response.Id);
        Assert.True(buildEventOrder < applyOrder);
        Assert.True(applyOrder < cleanupOrder);
        Assert.Contains(
            capturedMutation.DeleteDataElements,
            delete => delete.DataElement == fixture.DataElementInternal && delete.IgnoreLock
        );
        InstanceEvent deletedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Deleted.ToString(), deletedEvent.EventType);
        Assert.Equal(fixture.DataElementId.ToString(), deletedEvent.DataId);
        Assert.Equal(4, capturedMutation.ExpectedInstanceVersion);
        Assert.Equal(2, capturedMutation.ExpectedProcessStateVersion);
        Assert.Equal(
            "8",
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "6",
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
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
        fixture.InstanceRepository.Verify(
            repository =>
                repository.GetOne(fixture.InstanceGuid, false, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Delete_ImmediateDelete_WhenDeletedEventBuildFails_DoesNotApplyOrCleanup()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    fixture.InstanceInternal,
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.Id == fixture.DataElementId
                    )
                )
            )
            .Throws(new InvalidOperationException("missing user"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.Delete(
                fixture.InstanceOwnerPartyId,
                fixture.InstanceGuid,
                fixture.DataElementId,
                false,
                CancellationToken.None,
                ifInstanceVersionMatch: "4",
                ifProcessStateVersionMatch: "2"
            )
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
    }

    [Fact]
    public async Task Delete_ImmediateDelete_WhenAggregateApplyFails_DoesNotRunBlobCleanup()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("event insert failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.Delete(
                fixture.InstanceOwnerPartyId,
                fixture.InstanceGuid,
                fixture.DataElementId,
                false,
                CancellationToken.None,
                ifInstanceVersionMatch: "4",
                ifProcessStateVersionMatch: "2"
            )
        );

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
    }

    [Fact]
    public async Task Delete_ImmediateDelete_WhenVersionMismatch_ReturnsPreconditionFailedAndDoesNotCleanup()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InstanceVersionMismatchException(9, 4));

        ActionResult<DataElement> result = await fixture.Sut.Delete(
            fixture.InstanceOwnerPartyId,
            fixture.InstanceGuid,
            fixture.DataElementId,
            false,
            CancellationToken.None,
            ifInstanceVersionMatch: "4",
            ifProcessStateVersionMatch: "2"
        );

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, objectResult.StatusCode);
        Assert.Equal(
            "9",
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "4",
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
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
    }

    [Fact]
    public async Task Delete_ImmediateDelete_WhenRepositoryReportsConflict_ReturnsConflictAndDoesNotCleanup()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        string errorMessage = $"Data element {fixture.DataElementId} could not be deleted.";
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new RepositoryException(errorMessage, HttpStatusCode.Conflict));

        ActionResult<DataElement> result = await fixture.Sut.Delete(
            fixture.InstanceOwnerPartyId,
            fixture.InstanceGuid,
            fixture.DataElementId,
            false,
            CancellationToken.None,
            ifInstanceVersionMatch: "4",
            ifProcessStateVersionMatch: "2"
        );

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.Equal(errorMessage, objectResult.Value);
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
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_ProcessStatusAdmissionMismatch_ReturnsConflictBeforeApply(bool delay)
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        fixture.InstanceInternal.Process = new ProcessState { Status = ProcessStatus.Processing };

        ActionResult<DataElement> result = await fixture.Sut.Delete(
            fixture.InstanceOwnerPartyId,
            fixture.InstanceGuid,
            fixture.DataElementId,
            delay,
            CancellationToken.None,
            ifInstanceVersionMatch: "4",
            ifProcessStateVersionMatch: "2"
        );

        ObjectResult conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains(
            "processing",
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
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
    }

    [Fact]
    public async Task Delete_DelayedDelete_UsesCallerTimestampAndReturnsApplySnapshotElement()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        InstanceMutationCommit capturedMutation = null;
        DataElementInternal snapshotDataElement = fixture.DataElement.FromApiModel();
        snapshotDataElement.Tags = ["apply-snapshot"];
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(
                    false,
                    [],
                    new InstanceInternal
                    {
                        Data = [snapshotDataElement],
                        Versions = new StorageVersions(9, 7),
                    }
                )
            );

        ActionResult<DataElement> result = await fixture.Sut.Delete(
            fixture.InstanceOwnerPartyId,
            fixture.InstanceGuid,
            fixture.DataElementId,
            true,
            CancellationToken.None,
            ifInstanceVersionMatch: "4",
            ifProcessStateVersionMatch: "2"
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        DataElement response = Assert.IsType<DataElement>(ok.Value);
        Assert.Equal(["apply-snapshot"], response.Tags);
        Assert.NotNull(capturedMutation);
        Assert.NotNull(fixture.DataElementInternal.LastChanged);
        Assert.Equal(fixture.DataElementInternal.LastChanged, capturedMutation.LastChanged);
        Assert.Equal(fixture.DataElementInternal.LastChangedBy, capturedMutation.LastChangedBy);
        InstanceMutationDataElementUpdate update = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.True(update.IgnoreLock);
        Assert.Equal("9", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("7", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
    }

    [Fact]
    public async Task Delete_DelayedDelete_WhenApplySnapshotOmitsUpdatedElement_ThrowsInvariantFailure()
    {
        ImmediateDeleteFixture fixture = CreateImmediateDeleteFixture();
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    fixture.InstanceGuid,
                    fixture.InstanceInternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(
                    false,
                    [],
                    new InstanceInternal { Data = [], Versions = new StorageVersions(9, 7) }
                )
            );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                fixture.Sut.Delete(
                    fixture.InstanceOwnerPartyId,
                    fixture.InstanceGuid,
                    fixture.DataElementId,
                    true,
                    CancellationToken.None,
                    ifInstanceVersionMatch: "4",
                    ifProcessStateVersionMatch: "2"
                )
        );

        Assert.Equal(
            "Delayed-delete apply result did not include the updated data element.",
            exception.Message
        );
    }

    [Fact]
    public async Task Get_VerifyDataRepositoryUpdateInput()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch);

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        dataRepositoryMock.Verify(
            d =>
                d.UpdateReadStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    true,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Get_WithBlobVersionId_PassesVersionedPathToReadBlob()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        const string expectedBlobVersionId = "existing-version-id";
        Guid instanceGuid = Guid.NewGuid();
        Guid dataGuid = Guid.NewGuid();
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            "ttd/apps-test",
            instanceGuid,
            expectedBlobVersionId
        );
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(expectedPropertiesForPatch, blobVersionId: expectedBlobVersionId);

        // Act
        var result = await testController.Get(
            12345,
            instanceGuid,
            dataGuid,
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        blobRepositoryMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Get_WithBlobVersionId_EmitsETag()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        const string currentBlobVersionId = "existing-version-id";
        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            blobVersionId: currentBlobVersionId
        );

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        Assert.Equal(
            $"\"{currentBlobVersionId}\"",
            testController.Response.Headers[HeaderNames.ETag]
        );
    }

    [Fact]
    public async Task Get_WhenBlobIsMissing_ReturnsNotFoundWithVersionHeaders()
    {
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(
                ["/isRead"],
                blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7())
            );
        blobRepositoryMock
            .Setup(b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((Stream)null);

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("1", testController.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("1", testController.Response.Headers[StorageHeaders.ProcessStateVersion]);
    }

    [Fact]
    public async Task Get_OnDemandRequestThrows_DoesNotWriteVersionHeaders()
    {
        Mock<IOnDemandClient> onDemandClientMock = new();
        onDemandClientMock
            .Setup(c => c.GetStreamAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("on-demand request failed"));
        (DataController testController, _, _) = GetTestController(
            ["/isRead"],
            blobStoragePathOverride: "ondemand/formdatapdf",
            onDemandClient: onDemandClientMock.Object
        );

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            testController.Get(12345, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)
        );

        onDemandClientMock.Verify(c => c.GetStreamAsync(It.IsAny<string>()), Times.Once);
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
    }

    [Fact]
    public async Task Get_WithMatchingIfMatch_ReturnsFileAndEmitsETag()
    {
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        HeaderDictionary requestHeaders = new()
        {
            [HeaderNames.IfMatch] = $"\"{currentBlobVersionId}\"",
        };
        (DataController testController, _, _) = GetTestController(
            ["/isRead"],
            blobVersionId: currentBlobVersionId,
            requestHeaders: requestHeaders
        );

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal(
            $"\"{currentBlobVersionId}\"",
            testController.Response.Headers[HeaderNames.ETag]
        );
    }

    [Fact]
    public async Task Get_WithStaleIfMatch_ReturnsPreconditionFailedBeforeReadingBlob()
    {
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string staleBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        HeaderDictionary requestHeaders = new()
        {
            [HeaderNames.IfMatch] = $"\"{staleBlobVersionId}\"",
        };
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            ["/isRead"],
            blobVersionId: currentBlobVersionId,
            requestHeaders: requestHeaders
        );

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        StatusCodeResult preconditionFailed = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
        VerifyNoContentReadSideEffects(dataRepositoryMock, blobRepositoryMock);
    }

    [Fact]
    public async Task Get_WithIfMatchAndNoBlobVersion_ReturnsPreconditionFailedBeforeReadingBlob()
    {
        string expectedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        HeaderDictionary requestHeaders = new()
        {
            [HeaderNames.IfMatch] = $"\"{expectedBlobVersionId}\"",
        };
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(["/isRead"], blobVersionId: null, requestHeaders: requestHeaders);

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        StatusCodeResult preconditionFailed = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
        VerifyNoContentReadSideEffects(dataRepositoryMock, blobRepositoryMock);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"weak\"")]
    [InlineData("\"first\", \"second\"")]
    [InlineData("\"not-a-blob-version\"")]
    public async Task Get_WithInvalidIfMatch_ReturnsBadRequestBeforeReadingBlob(string ifMatch)
    {
        HeaderDictionary requestHeaders = new() { [HeaderNames.IfMatch] = ifMatch };
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            ["/isRead"],
            blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7()),
            requestHeaders: requestHeaders
        );

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoContentReadSideEffects(dataRepositoryMock, blobRepositoryMock);
    }

    [Theory]
    [InlineData("\"EREREREREREREREREREREQ\"")]
    [InlineData("*")]
    public async Task Get_HiddenHardDeletedElement_WithIfMatch_ReturnsNotFound(string ifMatch)
    {
        HeaderDictionary requestHeaders = new() { [HeaderNames.IfMatch] = ifMatch };
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            ["/isRead"],
            blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7()),
            requestHeaders: requestHeaders,
            isHardDeleted: true
        );

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("1", testController.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("1", testController.Response.Headers[StorageHeaders.ProcessStateVersion]);
        VerifyNoContentReadSideEffects(dataRepositoryMock, blobRepositoryMock);
    }

    [Fact]
    public async Task Get_WhenInstanceReadAuthorizationFails_IgnoresMalformedIfMatch()
    {
        HeaderDictionary requestHeaders = new() { [HeaderNames.IfMatch] = "*" };
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(["/isRead"], requestHeaders: requestHeaders, authorized: false);

        ActionResult result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        dataRepositoryMock.Verify(
            repository =>
                repository.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Get_WithoutBlobVersionId_OmitsETag()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            blobVersionId: null
        );

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        Assert.False(testController.Response.Headers.ContainsKey(HeaderNames.ETag));
    }

    [Fact]
    public async Task OverwriteData_VerifyDataRepositoryUpdateInput()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                true,
                blobVersionId: "existing-version-id"
            );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(p =>
                        VerifyPropertyListInput(
                            expectedPropertiesForPatch.Count,
                            expectedPropertiesForPatch,
                            p
                        )
                    ),
                    It.Is<DataElementUpdateContext>(o => o.EnforceLockCheck),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_WithoutIfMatch_UsesReadBlobVersionAsExpectedCurrentBlobVersion()
    {
        // Arrange
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: currentBlobVersionId
            );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.Is<DataElementUpdateContext>(o =>
                        o.EnforceLockCheck && o.ExpectedCurrentBlobVersion == null
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_WithIfMatch_UsesHeaderBlobVersionAsExpectedCurrentBlobVersion()
    {
        // Arrange
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string ifMatchBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        HeaderDictionary headers = new()
        {
            { HeaderNames.IfMatch, new StringValues($"\"{ifMatchBlobVersionId}\"") },
        };

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: currentBlobVersionId,
                requestHeaders: headers
            );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.Is<DataElementUpdateContext>(o =>
                        o.EnforceLockCheck && o.ExpectedCurrentBlobVersion == ifMatchBlobVersionId
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_InvalidIfMatch_ReturnsBadRequestBeforeUpload()
    {
        // Arrange
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = [];
        HeaderDictionary headers = new()
        {
            { HeaderNames.IfMatch, new StringValues("\"not-a-blob-version\"") },
        };

        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            blobVersionId: currentBlobVersionId,
            requestHeaders: headers
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("If-Match ETag value must be a blob version id.", badRequest.Value);
        blobRepositoryMock.Verify(
            b =>
                b.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task OverwriteData_Success_EmitsAllocatedBlobVersionETag()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7()),
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        Assert.Equal(
            $"\"{allocatedBlobVersionId}\"",
            testController.Response.Headers[HeaderNames.ETag]
        );
    }

    [Theory]
    [InlineData(DataWriteEndpoint.Overwrite, LateFailureDependency.FileScan)]
    [InlineData(DataWriteEndpoint.Overwrite, LateFailureDependency.EventDispatch)]
    [InlineData(DataWriteEndpoint.CreateAndUpload, LateFailureDependency.FileScan)]
    [InlineData(DataWriteEndpoint.CreateAndUpload, LateFailureDependency.EventDispatch)]
    public async Task DataWrite_LateFailure_DoesNotWriteVersionHeadersOrETag(
        DataWriteEndpoint endpoint,
        LateFailureDependency dependency
    )
    {
        Action<Mock<IDataService>> configureDataService = null;
        Action<Mock<IInstanceEventService>> configureInstanceEventService = null;

        if (dependency == LateFailureDependency.FileScan)
        {
            configureDataService = mock =>
                mock.Setup(d =>
                        d.StartFileScan(
                            It.IsAny<InstanceInternal>(),
                            It.IsAny<DataType>(),
                            It.IsAny<DataElementInternal>(),
                            It.IsAny<DateTimeOffset>(),
                            It.IsAny<int?>(),
                            It.IsAny<CancellationToken>()
                        )
                    )
                    .ThrowsAsync(new InvalidOperationException("file scan failed"));
        }
        else
        {
            InstanceEventType eventType =
                endpoint == DataWriteEndpoint.Overwrite
                    ? InstanceEventType.Saved
                    : InstanceEventType.Created;
            configureInstanceEventService = mock =>
                mock.Setup(e =>
                        e.DispatchEvent(
                            eventType,
                            It.IsAny<InstanceInternal>(),
                            It.IsAny<DataElementInternal>()
                        )
                    )
                    .ThrowsAsync(new InvalidOperationException("event dispatch failed"));
        }

        (DataController testController, _, _) = GetTestController(
            endpoint == DataWriteEndpoint.Overwrite ? _expectedPropertiesForOverwrite : [],
            includeRequestBody: true,
            blobVersionId: endpoint == DataWriteEndpoint.Overwrite
                ? BlobVersionId.Encode(Guid.CreateVersion7())
                : null,
            configureDataService: configureDataService,
            configureInstanceEventService: configureInstanceEventService
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (endpoint == DataWriteEndpoint.Overwrite)
            {
                await testController.OverwriteData(
                    _instanceOwnerPartyId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    CancellationToken.None
                );
            }
            else
            {
                await testController.CreateAndUploadData(
                    _instanceOwnerPartyId,
                    Guid.NewGuid(),
                    _dataType,
                    CancellationToken.None
                );
            }
        });

        if (endpoint == DataWriteEndpoint.Overwrite)
        {
            Assert.False(testController.Response.Headers.ContainsKey(HeaderNames.ETag));
        }
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
    }

    [Fact]
    public async Task OverwriteData_UsesUpdatedBlobVersionForFileScan()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        Mock<IDataService> dataServiceMock = null;
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: "existing-version-id",
                configureDataService: mock => dataServiceMock = mock,
                allocatedBlobVersionId: allocatedBlobVersionId
            );

        dataRepositoryMock
            .Setup(d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new DataElementInternal { BlobVersionId = allocatedBlobVersionId });

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataServiceMock.Verify(
            d =>
                d.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.Is<DataElementInternal>(de => de.BlobVersionId == allocatedBlobVersionId),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Update_VerifyDataRepositoryUpdateInput()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
        [
            "/locked",
            "/refs",
            "/references",
            "/tags",
            "/userDefinedMetadata",
            "/metadata",
            "/deleteStatus",
            "/lastChanged",
            "/lastChangedBy",
        ];

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch, true);

        var instanceGuid = Guid.NewGuid();
        var dataElementId = Guid.NewGuid();
        var input = new DataElement
        {
            Id = $"{dataElementId}",
            InstanceGuid = $"{instanceGuid}",
            DataType = _dataType,
        };

        // Act
        var result = await testController.Update(
            _instanceOwnerPartyId,
            instanceGuid,
            dataElementId,
            input,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(p =>
                        VerifyPropertyListInput(
                            expectedPropertiesForPatch.Count,
                            expectedPropertiesForPatch,
                            p
                        )
                    ),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Update_MetadataNotFound_ReturnsNotFound()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
        [
            "/locked",
            "/refs",
            "/references",
            "/tags",
            "/userDefinedMetadata",
            "/metadata",
            "/deleteStatus",
            "/lastChanged",
            "/lastChangedBy",
        ];

        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            true,
            repositoryExceptionOnUpdate: new RepositoryException(
                "Data element was not found.",
                HttpStatusCode.NotFound
            )
        );

        var instanceGuid = Guid.NewGuid();
        var dataElementId = Guid.NewGuid();
        var input = new DataElement
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };

        // Act
        var result = await testController.Update(
            _instanceOwnerPartyId,
            instanceGuid,
            dataElementId,
            input,
            CancellationToken.None
        );

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result.Result, exactMatch: false);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Update_ProcessStatusConflict_ReturnsConflictWithCurrentStatus()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
        [
            "/locked",
            "/refs",
            "/references",
            "/tags",
            "/userDefinedMetadata",
            "/metadata",
            "/deleteStatus",
            "/lastChanged",
            "/lastChangedBy",
        ];

        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            repositoryExceptionOnUpdate: new ProcessStatusConflictException(
                ProcessStatus.Processing
            )
        );
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElement input = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };

        // Act
        ActionResult<DataElement> result = await testController.Update(
            _instanceOwnerPartyId,
            instanceGuid,
            dataElementId,
            input,
            CancellationToken.None
        );

        // Assert
        ObjectResult conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains(
            "processing",
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Delete_Delayed_DoesNotUseLegacyRepositoryUpdate()
    {
        // Arrange
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController([]);

        // Act
        var result = await testController.Delete(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Delete_DelayedMarkNotFound_ReturnsNotFound()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
        [
            "/deleteStatus",
            "/lastChanged",
            "/lastChangedBy",
        ];
        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            repositoryExceptionOnUpdate: new RepositoryException(
                "Data element was not found.",
                HttpStatusCode.NotFound
            )
        );

        // Act
        var result = await testController.Delete(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            CancellationToken.None
        );

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result.Result, exactMatch: false);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task SetFileScanStatus_WithoutBlobVersion_DelegatesToRepository()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/fileScanResult"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch);

        // Act
        var result = await testController.SetFileScanStatus(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new FileScanStatus { FileScanResult = FileScanResult.Infected }
        );

        // Assert
        Assert.True(result is OkResult { StatusCode: StatusCodes.Status200OK });
        dataRepositoryMock.Verify(
            d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<FileScanStatus>(s =>
                        s.FileScanResult == FileScanResult.Infected
                        && string.IsNullOrEmpty(s.BlobVersionId)
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task SetFileScanStatus_WithBlobVersion_DelegatesToRepository()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/fileScanResult"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch, blobVersionId: "current-version-id");
        StorageVersions expectedVersions = new(12, 7);
        dataRepositoryMock
            .Setup(d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<FileScanStatus>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                Task.FromResult(
                    new DataElementWriteResult(new DataElementInternal(), expectedVersions)
                )
            );

        // Act
        var result = await testController.SetFileScanStatus(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Infected,
                BlobVersionId = "current-version-id",
            }
        );

        // Assert
        Assert.True(result is OkResult { StatusCode: StatusCodes.Status200OK });
        Assert.Equal(
            "12",
            testController.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "7",
            testController.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
        dataRepositoryMock.Verify(
            d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<FileScanStatus>(s =>
                        s.FileScanResult == FileScanResult.Infected
                        && s.BlobVersionId == "current-version-id"
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task SetFileScanStatus_MissingElement_ReturnsOk()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/fileScanResult"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch);
        dataRepositoryMock
            .Setup(d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<FileScanStatus>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.FromResult<DataElementWriteResult>(null));

        // Act
        ActionResult result = await testController.SetFileScanStatus(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new FileScanStatus { FileScanResult = FileScanResult.Infected }
        );

        // Assert
        Assert.IsType<OkResult>(result);
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
    }

    [Fact]
    public async Task SetFileScanStatus_InvalidBlobVersion_ReturnsBadRequest()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/fileScanResult"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch);
        dataRepositoryMock
            .Setup(d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<FileScanStatus>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new RepositoryException("Invalid blob version", HttpStatusCode.BadRequest)
            );

        // Act
        ActionResult result = await testController.SetFileScanStatus(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Infected,
                BlobVersionId = "not-a-valid-version",
            }
        );

        // Assert
        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Equal("Invalid blob version", objectResult.Value);
    }

    [Fact]
    public async Task Get_IsReadUpdateNotFound_ReturnsNotFound()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            repositoryExceptionOnUpdate: new RepositoryException(
                "Data element was not found.",
                HttpStatusCode.NotFound
            )
        );

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
        dataRepositoryMock.Verify(
            d =>
                d.UpdateReadStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    true,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Get_AlreadyReadDataElement_ReturnsFile_WithoutUpdate()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch, isRead: true);

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        dataRepositoryMock.Verify(
            d =>
                d.UpdateReadStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAndUploadData_CreateMetadataThrows_DoesNotDeleteExplicitVersionBlob()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        Mock<IDataService> dataServiceMock = null;
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                throwOnCreate: true,
                configureDataService: mock => dataServiceMock = mock
            );

        // Act/assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.CreateAndUploadData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                _dataType,
                CancellationToken.None
            )
        );

        dataServiceMock.Verify(
            d =>
                d.UploadDataAndCreateDataElement(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<long>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAndUploadData_Success_PersistsAndQueuesBlobVersionId()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = ["/isRead"];
        Mock<IDataService> dataServiceMock = null;
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                configureDataService: mock => dataServiceMock = mock,
                allocatedBlobVersionId: allocatedBlobVersionId
            );

        // Act
        ActionResult<DataElement> result = await testController.CreateAndUploadData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            _dataType,
            CancellationToken.None
        );

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result.Result);
        var createdElement = Assert.IsType<DataElement>(createdResult.Value);
        Assert.Equal(allocatedBlobVersionId, createdElement.BlobVersionId);
        Assert.EndsWith(
            $"/data-elements/{allocatedBlobVersionId}",
            createdElement.BlobStoragePath,
            StringComparison.Ordinal
        );

        dataServiceMock.Verify(
            d =>
                d.UploadDataAndCreateDataElement(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.Is<DataElementCreateOptions>(options =>
                        options.DataElementId != Guid.Empty
                        && options.DataType == _dataType
                        && options.ContentType == "application/pdf"
                        && options.Filename == "filename.jpg"
                    ),
                    It.IsAny<long>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Once
        );
        dataServiceMock.Verify(
            d =>
                d.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.Is<DataElementInternal>(de => de.BlobVersionId == allocatedBlobVersionId),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateAndUploadData_ProcessStatusConflict_ReturnsConflictWithCurrentStatus()
    {
        // Arrange
        (DataController testController, _, _) = GetTestController(
            [],
            includeRequestBody: true,
            configureDataService: dataService =>
                dataService
                    .Setup(service =>
                        service.UploadDataAndCreateDataElement(
                            It.IsAny<InstanceInternal>(),
                            It.IsAny<Stream>(),
                            It.IsAny<DataElementCreateOptions>(),
                            It.IsAny<long>(),
                            It.IsAny<int?>(),
                            It.IsAny<CancellationToken>(),
                            It.IsAny<int?>(),
                            It.IsAny<int?>()
                        )
                    )
                    .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing))
        );

        // Act
        ActionResult<DataElement> result = await testController.CreateAndUploadData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            _dataType,
            CancellationToken.None
        );

        // Assert
        ObjectResult conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains(
            "processing",
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task OverwriteData_UpdateMetadataThrows_DoesNotDeleteExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            throwOnUpdate: true,
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act/assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.OverwriteData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            )
        );

        blobRepositoryMock.Verify(
            b =>
                b.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task OverwriteData_UpdateMetadataConflict_DeletesExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            repositoryExceptionOnUpdate: new RepositoryException(
                "Data element is locked and cannot be updated.",
                HttpStatusCode.Conflict
            ),
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.IsType<ConflictObjectResult>(result.Result);
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_ProcessStatusConflict_ReturnsCurrentStatusAndDeletesAllocatedBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            _expectedPropertiesForOverwrite,
            includeRequestBody: true,
            repositoryExceptionOnUpdate: new ProcessStatusConflictException(
                ProcessStatus.Processing
            ),
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act
        ActionResult<DataElement> result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains(
            "processing",
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_UpdateMetadataConflictWithIfMatch_ReturnsPreconditionFailedAndDeletesExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string ifMatchBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;
        HeaderDictionary headers = new()
        {
            { HeaderNames.IfMatch, new StringValues($"\"{ifMatchBlobVersionId}\"") },
        };

        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            repositoryExceptionOnUpdate: new DataElementBlobVersionMismatchException(
                "Data element current blob version did not match expected version.",
                1,
                1
            ),
            blobVersionId: currentBlobVersionId,
            allocatedBlobVersionId: allocatedBlobVersionId,
            requestHeaders: headers
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        var preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_UpdateMetadataNotFound_DeletesExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            repositoryExceptionOnUpdate: new RepositoryException(
                "Data element was not found.",
                HttpStatusCode.NotFound
            ),
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_WriteBlobThrows_DeletesExplicitVersionBlobAllocation()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = [];
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            throwOnWriteBlob: true,
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act/assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.OverwriteData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            )
        );

        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task OverwriteData_ZeroLengthBlob_DeletesExplicitVersionBlobAllocation()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = [];
        (
            DataController testController,
            Mock<IDataRepository> dataRepositoryMock,
            Mock<IBlobRepository> blobRepositoryMock
        ) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            blobWriteSize: 0,
            blobVersionId: "existing-version-id",
            allocatedBlobVersionId: allocatedBlobVersionId
        );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal("Could not process attached file", unprocessable.Value);
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAndUploadData_ZeroLengthBlob_DoesNotDeleteExplicitVersionBlob()
    {
        List<string> expectedPropertiesForPatch = ["/isRead"];
        Mock<IDataService> dataServiceMock = null;
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                configureDataService: mock =>
                {
                    dataServiceMock = mock;
                    mock.Setup(d =>
                            d.UploadDataAndCreateDataElement(
                                It.IsAny<InstanceInternal>(),
                                It.IsAny<Stream>(),
                                It.IsAny<DataElementCreateOptions>(),
                                It.IsAny<long>(),
                                It.IsAny<int?>(),
                                It.IsAny<CancellationToken>(),
                                null,
                                null
                            )
                        )
                        .ThrowsAsync(
                            new InvalidDataException("Empty stream provided. Cannot persist data.")
                        );
                }
            );

        var result = await testController.CreateAndUploadData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            _dataType,
            CancellationToken.None
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("Empty stream provided. Cannot persist data.", badRequest.Value);
        dataServiceMock.Verify(
            d =>
                d.UploadDataAndCreateDataElement(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<Stream>(),
                    It.IsAny<DataElementCreateOptions>(),
                    It.IsAny<long>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Once
        );
        blobRepositoryMock.Verify(
            b => b.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task OverwriteData_NullExistingBlobVersionId_StoresNewBlobVersionId()
    {
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch = _expectedPropertiesForOverwrite;

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: null,
                allocatedBlobVersionId: allocatedBlobVersionId
            );

        // Act
        var result = await testController.OverwriteData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.True(result.Result is OkObjectResult { StatusCode: StatusCodes.Status200OK });

        dataRepositoryMock.Verify(
            d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(p =>
                        p.ContainsKey("/currentBlobVersion")
                        && (string)p["/currentBlobVersion"] == allocatedBlobVersionId
                        && p.ContainsKey("/blobStoragePath")
                        && ((string)p["/blobStoragePath"]).EndsWith(
                            $"/data-elements/{allocatedBlobVersionId}",
                            StringComparison.Ordinal
                        )
                    ),
                    It.Is<DataElementUpdateContext>(o => o.EnforceLockCheck),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private static void VerifyNoContentReadSideEffects(
        Mock<IDataRepository> dataRepositoryMock,
        Mock<IBlobRepository> blobRepositoryMock
    )
    {
        dataRepositoryMock.Verify(
            repository =>
                repository.UpdateReadStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    true,
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        blobRepositoryMock.Verify(
            repository =>
                repository.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    private static bool VerifyPropertyListInput(
        int expectedPropCount,
        List<string> expectedProperties,
        Dictionary<string, object> propertyList
    )
    {
        if (propertyList.Count != expectedPropCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(propertyList),
                "Property list does not contain expected number of properties"
            );
        }

        foreach (string expectedProp in expectedProperties)
        {
            if (!propertyList.ContainsKey(expectedProp))
            {
                return false;
            }
        }

        if (propertyList.Keys.Except(expectedProperties).Intersect(_forbiddenUpdateProps).Any())
        {
            throw new ArgumentException(
                "Forbidden property attempted updated in dataElement. Check `_forbiddenUpdateProps` for reference",
                nameof(propertyList)
            );
        }

        return true;
    }

    private (
        DataController TestController,
        Mock<IDataRepository> DataRepositoryMock,
        Mock<IBlobRepository> BlobRepositoryMock
    ) GetTestController(
        List<string> expectedPropertiesForPatch,
        bool includeRequestBody = false,
        bool isRead = false,
        string blobVersionId = null,
        bool throwOnUpdate = false,
        bool throwOnCreate = false,
        RepositoryException repositoryExceptionOnUpdate = null,
        bool throwOnWriteBlob = false,
        long blobWriteSize = 123145864564,
        Action<Mock<IDataService>> configureDataService = null,
        string allocatedBlobVersionId = null,
        HeaderDictionary requestHeaders = null,
        bool isHardDeleted = false,
        bool authorized = true,
        Action<Mock<IInstanceEventService>> configureInstanceEventService = null,
        string blobStoragePathOverride = null,
        IOnDemandClient onDemandClient = null
    )
    {
        allocatedBlobVersionId ??= BlobVersionId.Encode(Guid.CreateVersion7());
        requestHeaders ??= [];

        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IInstanceEventService> instanceEventServiceMock = new();
        Mock<IDataService> dataServiceMock = new();
        Mock<IAuthorization> authorizationServiceMock = new();

        var updateSetup = dataRepositoryMock.Setup(d =>
            d.Update(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(propertyList =>
                    VerifyPropertyListInput(
                        expectedPropertiesForPatch.Count,
                        expectedPropertiesForPatch,
                        propertyList
                    )
                ),
                It.IsAny<DataElementUpdateContext>(),
                It.IsAny<CancellationToken>()
            )
        );

        if (repositoryExceptionOnUpdate != null)
        {
            updateSetup.ThrowsAsync(repositoryExceptionOnUpdate);
        }
        else if (throwOnUpdate)
        {
            updateSetup.ThrowsAsync(new InvalidOperationException("metadata update failed"));
        }
        else
        {
            updateSetup.ReturnsAsync(new DataElement());
        }

        var readStatusSetup = dataRepositoryMock.Setup(d =>
            d.UpdateReadStatus(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                true,
                It.IsAny<CancellationToken>()
            )
        );

        if (repositoryExceptionOnUpdate != null)
        {
            readStatusSetup.ThrowsAsync(repositoryExceptionOnUpdate);
        }
        else if (throwOnUpdate)
        {
            readStatusSetup.ThrowsAsync(new InvalidOperationException("metadata update failed"));
        }
        else
        {
            readStatusSetup.ReturnsAsync(new DataElement());
        }

        dataRepositoryMock
            .Setup(d =>
                d.CreateBlobVersionId(
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
            .Setup(d =>
                d.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);

        dataRepositoryMock
            .Setup(d => d.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (Guid instanceGuid, Guid dataElementId, CancellationToken cancellationToken) =>
                {
                    string legacyBlobStoragePath =
                        $"ttd/apps-test/{instanceGuid}/data/{dataElementId}";
                    string blobStoragePath =
                        blobStoragePathOverride
                        ?? (
                            string.IsNullOrEmpty(blobVersionId)
                                ? legacyBlobStoragePath
                                : BlobRepository.GetVersionedBlobPath(
                                    "ttd/apps-test",
                                    instanceGuid,
                                    blobVersionId
                                )
                        );

                    return new DataElement
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                        DataType = _dataType,
                        IsRead = isRead,
                        ContentType = "application/octet-stream",
                        BlobStoragePath = blobStoragePath,
                        DeleteStatus = isHardDeleted
                            ? new DeleteStatus { IsHardDeleted = true }
                            : null,
                    }.FromApiModel(blobVersionId);
                }
            );

        dataRepositoryMock
            .Setup(d =>
                d.UpdateFileScanStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<FileScanStatus>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (
                    Guid instanceGuid,
                    Guid dataElementId,
                    FileScanStatus fileScanStatus,
                    CancellationToken _
                ) =>
                    new DataElement
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                        FileScanResult = fileScanStatus.FileScanResult,
                    }
            );

        blobRepositoryMock
            .Setup(d =>
                d.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));

        var writeBlobSetup = blobRepositoryMock.Setup(d =>
            d.WriteBlob(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<int?>()
            )
        );

        if (throwOnWriteBlob)
        {
            writeBlobSetup.ThrowsAsync(new InvalidOperationException("blob write failed"));
        }
        else
        {
            writeBlobSetup.ReturnsAsync((blobWriteSize, DateTimeOffset.Now));
        }

        blobRepositoryMock
            .Setup(d => d.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        instanceRepositoryMock
            .Setup(ir =>
                ir.GetOne(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                (
                    Guid instanceGuid,
                    bool includeDataElements,
                    CancellationToken cancellationToken
                ) => CreateInstanceInternal(instanceGuid, includeDataElements)
            );

        applicationRepositoryMock
            .Setup(ar =>
                ar.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new Application
                {
                    DataTypes =
                    [
                        new DataType
                        {
                            Id = _dataType,
                            AppLogic = new ApplicationLogic { AutoDeleteOnProcessEnd = true },
                        },
                    ],
                }
            );

        instanceEventServiceMock.Setup(ier =>
            ier.DispatchEvent(
                It.IsAny<InstanceEventType>(),
                It.IsAny<InstanceInternal>(),
                It.IsAny<DataElementInternal>()
            )
        );
        configureInstanceEventService?.Invoke(instanceEventServiceMock);

        dataServiceMock.Setup(d =>
            d.StartFileScan(
                It.IsAny<InstanceInternal>(),
                It.IsAny<DataType>(),
                It.IsAny<DataElementInternal>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()
            )
        );
        var uploadSetup = dataServiceMock.Setup(d =>
            d.UploadDataAndCreateDataElement(
                It.IsAny<InstanceInternal>(),
                It.IsAny<Stream>(),
                It.IsAny<DataElementCreateOptions>(),
                It.IsAny<long>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>(),
                null,
                null
            )
        );
        if (throwOnCreate)
        {
            uploadSetup.ThrowsAsync(new InvalidOperationException("metadata create failed"));
        }
        else
        {
            uploadSetup.ReturnsAsync(
                (
                    InstanceInternal instanceInternal,
                    Stream stream,
                    DataElementCreateOptions options,
                    long instanceInternalId,
                    int? storageAccountNumber,
                    CancellationToken cancellationToken
                ) =>
                {
                    string instanceGuid = instanceInternal.Id.ToString();
                    DataElement dataElement = new()
                    {
                        Id = options.DataElementId.ToString(),
                        InstanceGuid = instanceGuid,
                        DataType = options.DataType,
                        ContentType = options.ContentType,
                        Filename = options.Filename,
                        Created = options.Created,
                        CreatedBy = options.CreatedBy,
                        LastChanged = options.Created,
                        LastChangedBy = options.CreatedBy,
                        Refs = options.Refs,
                        Size = 123145864564,
                        BlobStoragePath = BlobRepository.GetVersionedBlobPath(
                            instanceInternal.AppId,
                            new Guid(instanceGuid),
                            allocatedBlobVersionId
                        ),
                        FileScanResult = options.FileScanResult,
                        IsRead = options.IsRead,
                    };

                    return (dataElement.FromApiModel(allocatedBlobVersionId), DateTimeOffset.Now);
                }
            );
        }
        configureDataService?.Invoke(dataServiceMock);

        authorizationServiceMock
            .Setup(a =>
                a.AuthorizeEnrichedInstanceAction(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .ReturnsAsync(authorized);

        Mock<HttpContext> httpContextMock = new();
        httpContextMock.Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

        Mock<HttpRequest> requestMock = new();
        requestMock.Setup(r => r.Headers).Returns(requestHeaders);
        requestMock.Setup(r => r.Cookies).Returns(Mock.Of<IRequestCookieCollection>());

        if (includeRequestBody)
        {
            requestMock.Setup(r => r.ContentType).Returns("application/pdf");
            requestHeaders["Content-Disposition"] = new StringValues(
                "attachment; filename=\"filename.jpg\"; size=12348"
            );
            requestMock
                .Setup(r => r.Body)
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));
        }

        httpContextMock.Setup(c => c.Request).Returns(requestMock.Object);
        Mock<HttpResponse> responseMock = new();
        responseMock.Setup(r => r.Headers).Returns(new HeaderDictionary());
        httpContextMock.Setup(c => c.Response).Returns(responseMock.Object);

        ControllerContext controllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        IOptions<GeneralSettings> generalSettings = Options.Create(
            new GeneralSettings { Hostname = "https://altinn.no/" }
        );
        Mock<IInstanceMutationRepository> instanceMutationRepositoryMock = new();
        var applySetup = instanceMutationRepositoryMock.Setup(repository =>
            repository.Apply(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<InstanceMutationCommit>(),
                It.IsAny<CancellationToken>()
            )
        );
        if (repositoryExceptionOnUpdate != null)
        {
            applySetup.ThrowsAsync(repositoryExceptionOnUpdate);
        }
        else if (throwOnUpdate)
        {
            applySetup.ThrowsAsync(new InvalidOperationException("metadata update failed"));
        }
        else
        {
            applySetup.ReturnsAsync(
                (
                    Guid mutatedInstanceGuid,
                    long _,
                    InstanceMutationCommit mutation,
                    CancellationToken _
                ) =>
                {
                    InstanceInternal snapshot = CreateInstanceInternal(mutatedInstanceGuid, true);
                    foreach (
                        InstanceMutationDataElementUpdate update in mutation.UpdateDataElements
                    )
                    {
                        if (
                            snapshot.Data.All(dataElement => dataElement.Id != update.DataElementId)
                        )
                        {
                            snapshot.Data.Add(
                                new DataElementInternal
                                {
                                    Id = update.DataElementId,
                                    InstanceGuid = mutatedInstanceGuid,
                                    DataType = _dataType,
                                }
                            );
                        }
                    }

                    return new InstanceMutationApplyResult(false, [], snapshot);
                }
            );
        }

        var sut = new DataController(
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceRepositoryMock.Object,
            instanceMutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            dataServiceMock.Object,
            instanceEventServiceMock.Object,
            generalSettings,
            onDemandClient,
            authorizationServiceMock.Object
        )
        {
            ControllerContext = controllerContext,
        };

        return (sut, dataRepositoryMock, blobRepositoryMock);
    }

    private ImmediateDeleteFixture CreateImmediateDeleteFixture()
    {
        const int instanceOwnerPartyId = 555;
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            LastChangedBy = "previous-user",
        };
        DataElementInternal dataElementInternal = dataElement.FromApiModel();
        Instance instance = new()
        {
            Id = $"{instanceOwnerPartyId}/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = instanceOwnerPartyId.ToString() },
            Org = _org,
            AppId = _appId,
            Data = [dataElement],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [dataElementInternal],
            InternalId: 123L,
            versions: new StorageVersions(5, 3)
        );

        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IDataService> dataServiceMock = new();
        Mock<IInstanceEventService> instanceEventServiceMock = new();
        Mock<IAuthorization> authorizationServiceMock = new();

        dataRepositoryMock
            .Setup(repository =>
                repository.Read(instanceGuid, dataElementId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(dataElementInternal);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(instanceInternal);
        applicationRepositoryMock
            .Setup(repository => repository.FindOne(_appId, _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new Application
                {
                    Id = _appId,
                    Org = _org,
                    StorageAccountNumber = 7,
                    DataTypes =
                    [
                        new DataType
                        {
                            Id = _dataType,
                            AppLogic = new ApplicationLogic { AutoDeleteOnProcessEnd = true },
                        },
                    ],
                }
            );

        DefaultHttpContext httpContext = new()
        {
            User = PrincipalUtil.GetPrincipal(200001, instanceOwnerPartyId),
        };
        DataController sut = new(
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceRepositoryMock.Object,
            mutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            dataServiceMock.Object,
            instanceEventServiceMock.Object,
            Options.Create(new GeneralSettings { Hostname = "https://altinn.no/" }),
            null,
            authorizationServiceMock.Object
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        InstanceEvent deletedEvent = new()
        {
            EventType = InstanceEventType.Deleted.ToString(),
            DataId = dataElementId.ToString(),
        };
        ImmediateDeleteFixture fixture = new(
            sut,
            httpContext,
            instanceOwnerPartyId,
            instanceGuid,
            123L,
            dataElementId,
            instanceInternal,
            instance,
            dataElement,
            dataElementInternal,
            dataRepositoryMock,
            instanceRepositoryMock,
            mutationRepositoryMock,
            dataServiceMock,
            instanceEventServiceMock,
            deletedEvent
        );
        instanceEventServiceMock
            .Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    instanceInternal,
                    dataElementInternal
                )
            )
            .Callback(() => fixture.OnBuildDeletedEvent())
            .Returns(deletedEvent);
        return fixture;
    }

    private sealed class ImmediateDeleteFixture(
        DataController sut,
        DefaultHttpContext httpContext,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        long instanceInternalId,
        Guid dataElementId,
        InstanceInternal instanceInternal,
        Instance instance,
        DataElement dataElement,
        DataElementInternal dataElementInternal,
        Mock<IDataRepository> dataRepository,
        Mock<IInstanceRepository> instanceRepository,
        Mock<IInstanceMutationRepository> mutationRepository,
        Mock<IDataService> dataService,
        Mock<IInstanceEventService> instanceEventService,
        InstanceEvent deletedEvent
    )
    {
        public DataController Sut { get; } = sut;

        public DefaultHttpContext HttpContext { get; } = httpContext;

        public int InstanceOwnerPartyId { get; } = instanceOwnerPartyId;

        public Guid InstanceGuid { get; } = instanceGuid;

        public long InstanceInternalId { get; } = instanceInternalId;

        public Guid DataElementId { get; } = dataElementId;

        public InstanceInternal InstanceInternal { get; } = instanceInternal;

        public Instance Instance { get; } = instance;

        public DataElement DataElement { get; } = dataElement;

        public DataElementInternal DataElementInternal { get; } = dataElementInternal;

        public Mock<IDataRepository> DataRepository { get; } = dataRepository;

        public Mock<IInstanceRepository> InstanceRepository { get; } = instanceRepository;

        public Mock<IInstanceMutationRepository> MutationRepository { get; } = mutationRepository;

        public Mock<IDataService> DataService { get; } = dataService;

        public Mock<IInstanceEventService> InstanceEventService { get; } = instanceEventService;

        public InstanceEvent DeletedEvent { get; } = deletedEvent;

        public Action OnBuildDeletedEvent { get; set; } = () => { };
    }

    private InstanceInternal CreateInstanceInternal(Guid instanceGuid, bool includeDataElements)
    {
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = includeDataElements ? GetDataElements(instanceGuid) : null,
        };

        List<DataElementInternal> dataElements =
            instance.Data?.Select(dataElement => dataElement.FromApiModel()).ToList() ?? [];

        return InstanceInternalTestFactory.Create(instance, dataElements, InternalId: 123L);
    }

    private static List<DataElement> GetDataElements(Guid instanceGuid)
    {
        List<DataElement> dataElements = [];
        string dataElementsPath = GetDataElementsPath();

        string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
        foreach (string elementPath in dataElementPaths)
        {
            string content = File.ReadAllText(elementPath);
            DataElement dataElement = JsonSerializer.Deserialize<DataElement>(content, _options);
            if (dataElement.InstanceGuid == instanceGuid.ToString())
            {
                dataElements.Add(dataElement);
            }
        }

        return dataElements;
    }

    private static string GetDataElementsPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(DataControllerUnitTests).Assembly.Location).LocalPath
        );
        return Path.Combine(
            unitTestFolder,
            "..",
            "..",
            "..",
            "data",
            "postgresdata",
            "dataelements"
        );
    }
}
