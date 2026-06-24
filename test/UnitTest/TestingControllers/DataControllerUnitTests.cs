#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
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
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class DataControllerUnitTests
{
    private static List<string> _forbiddenUpdateProps =
    [
        "/created",
        "/createdBy",
        "/id",
        "/instanceGuid",
        "/blobStoragePath",
        "/dataType",
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
    public async Task Get_WithBlobVersionId_PassesVersionedPathToReadBlob()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        const string expectedBlobVersionId = "existing-version-id";
        Guid instanceGuid = Guid.NewGuid();
        Guid dataGuid = Guid.NewGuid();
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            "ttd/apps-test",
            instanceGuid.ToString(),
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
    public async Task OverwriteData_VerifyDataRepositoryUpdateInput()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
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
    public async Task OverwriteData_UsesUpdatedBlobVersionForFileScan()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch =
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
            .ReturnsAsync(
                (
                    Guid instanceGuid,
                    Guid dataElementId,
                    Dictionary<string, object> propertyList,
                    DataElementUpdateContext context,
                    CancellationToken _
                ) =>
                    new DataElement
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                    }
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
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Delete_VerifyDataRepositoryUpdateInput()
    {
        // Arrange
        List<string> expectedPropertiesForPatch =
        [
            "/deleteStatus",
            "/lastChanged",
            "/lastChangedBy",
        ];
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(expectedPropertiesForPatch);

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
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
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
    }

    [Fact]
    public async Task Get_UnreadDataElement_ReturnsFile_UpdatesIsRead()
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
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(p =>
                        p.Count == 1 && p.ContainsKey("/isRead")
                    ),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
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
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(p =>
                        p.Count == 1 && p.ContainsKey("/isRead")
                    ),
                    It.IsAny<DataElementUpdateContext>(),
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
                    It.IsAny<CancellationToken>()
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
        Assert.DoesNotContain("blobVersionId", JsonSerializer.Serialize(createdElement));
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
                    It.IsAny<CancellationToken>()
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
    public async Task OverwriteData_UpdateMetadataThrows_DoesNotDeleteExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch =
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
        List<string> expectedPropertiesForPatch =
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
    public async Task OverwriteData_UpdateMetadataNotFound_DeletesExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        List<string> expectedPropertiesForPatch =
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
                                It.IsAny<CancellationToken>()
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
                    It.IsAny<CancellationToken>()
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
        List<string> expectedPropertiesForPatch =
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
        string allocatedBlobVersionId = null
    )
    {
        allocatedBlobVersionId ??= BlobVersionId.Encode(Guid.CreateVersion7());

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

        var createSetup = dataRepositoryMock.Setup(d =>
            d.Create(
                It.IsAny<DataElementInternal>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()
            )
        );

        if (throwOnCreate)
        {
            createSetup.ThrowsAsync(new InvalidOperationException("metadata create failed"));
        }
        else
        {
            createSetup.ReturnsAsync((DataElementInternal de, long _, CancellationToken _) => de);
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
                    string blobStoragePath = string.IsNullOrEmpty(blobVersionId)
                        ? legacyBlobStoragePath
                        : BlobRepository.GetVersionedBlobPath(
                            "ttd/apps-test",
                            instanceGuid.ToString(),
                            blobVersionId
                        );

                    return new DataElementInternal(
                        new DataElement
                        {
                            Id = dataElementId.ToString(),
                            InstanceGuid = instanceGuid.ToString(),
                            DataType = _dataType,
                            IsRead = isRead,
                            ContentType = "application/octet-stream",
                            BlobStoragePath = blobStoragePath,
                        },
                        blobVersionId
                    );
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
                It.IsAny<Instance>(),
                It.IsAny<DataElement>()
            )
        );

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
                It.IsAny<CancellationToken>()
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
                    Instance instance = instanceInternal.Instance;
                    string instanceGuid = instance.Id.Split('/')[1];
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
                            instance.AppId,
                            instanceGuid,
                            allocatedBlobVersionId
                        ),
                        FileScanResult = options.FileScanResult,
                        IsRead = options.IsRead,
                    };

                    return (
                        new DataElementInternal(dataElement, allocatedBlobVersionId),
                        DateTimeOffset.Now
                    );
                }
            );
        }
        configureDataService?.Invoke(dataServiceMock);

        authorizationServiceMock
            .Setup(a => a.AuthorizeEnrichedInstanceAction(It.IsAny<Instance>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        Mock<HttpContext> httpContextMock = new();
        httpContextMock.Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

        Mock<HttpRequest> requestMock = new();
        requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());

        if (includeRequestBody)
        {
            requestMock.Setup(r => r.ContentType).Returns("application/pdf");
            requestMock
                .Setup(r => r.Headers)
                .Returns(
                    new HeaderDictionary()
                    {
                        {
                            "Content-Disposition",
                            new StringValues("attachment; filename=\"filename.jpg\"; size=12348")
                        },
                    }
                );
            requestMock
                .Setup(r => r.Body)
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));
        }

        httpContextMock.Setup(c => c.Request).Returns(requestMock.Object);

        ControllerContext controllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        IOptions<GeneralSettings> generalSettings = Options.Create(
            new GeneralSettings { Hostname = "https://altinn.no/" }
        );

        var sut = new DataController(
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            dataServiceMock.Object,
            instanceEventServiceMock.Object,
            generalSettings,
            null,
            authorizationServiceMock.Object
        )
        {
            ControllerContext = controllerContext,
        };

        return (sut, dataRepositoryMock, blobRepositoryMock);
    }

    private (InstanceInternal Instance, long InternalId) CreateInstanceInternal(
        Guid instanceGuid,
        bool includeDataElements
    )
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
            instance
                .Data?.Select(dataElement => new DataElementInternal(dataElement, null))
                .ToList()
            ?? [];

        return (new InstanceInternal(instance, dataElements), 0);
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
            if (dataElement.InstanceGuid.Contains(instanceGuid.ToString()))
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
