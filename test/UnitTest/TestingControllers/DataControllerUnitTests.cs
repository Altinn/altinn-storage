#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Get_WithBlobVersionId_PassesVersionIdToReadBlob()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        const string expectedBlobVersionId = "existing-version-id";
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(expectedPropertiesForPatch, blobVersionId: expectedBlobVersionId);

        // Act
        var result = await testController.Get(
            12345,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        // Assert
        Assert.True(result is FileStreamResult);
        blobRepositoryMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    expectedBlobVersionId,
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
            "/blobVersionId",
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_UsesUpdatedBlobVersionForFileScan()
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
            "/blobVersionId",
        ];

        Mock<IDataService> dataServiceMock = null;
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: "existing-version-id",
                configureDataService: mock => dataServiceMock = mock
            );

        dataRepositoryMock
            .Setup(d =>
                d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (
                    Guid instanceGuid,
                    Guid dataElementId,
                    Dictionary<string, object> propertyList,
                    CancellationToken _
                ) =>
                    new DataElement
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                        BlobVersionId = (string)propertyList["/blobVersionId"],
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
                    It.IsAny<Instance>(),
                    It.IsAny<DataType>(),
                    It.Is<DataElement>(de => de.BlobVersionId == "mock-version-id"),
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAndUploadData_CreateMetadataThrows_CleansUpBlob()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                throwOnCreate: true
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
            b =>
                b.DeleteBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    "mock-version-id"
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateAndUploadData_Success_PersistsAndQueuesBlobVersionId()
    {
        // Arrange
        List<string> expectedPropertiesForPatch = ["/isRead"];
        Mock<IDataService> dataServiceMock = null;
        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                configureDataService: mock => dataServiceMock = mock
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
        Assert.Equal("mock-version-id", createdElement.BlobVersionId);

        dataRepositoryMock.Verify(
            d =>
                d.Create(
                    It.Is<DataElement>(de => de.BlobVersionId == "mock-version-id"),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        dataServiceMock.Verify(
            d =>
                d.StartFileScan(
                    It.IsAny<Instance>(),
                    It.IsAny<DataType>(),
                    It.Is<DataElement>(de => de.BlobVersionId == "mock-version-id"),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task OverwriteData_UpdateMetadataThrows_CleansUpBlob()
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
            "/blobVersionId",
        ];

        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                throwOnUpdate: true,
                blobVersionId: "existing-version-id"
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
            b =>
                b.DeleteBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    "mock-version-id"
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateAndUploadData_ZeroLengthDeleteBlobThrows_StillReturnsBadRequest()
    {
        List<string> expectedPropertiesForPatch = ["/isRead"];
        (DataController testController, _, Mock<IBlobRepository> blobRepositoryMock) =
            GetTestController(expectedPropertiesForPatch, includeRequestBody: true);

        blobRepositoryMock
            .Setup(b =>
                b.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((0, DateTimeOffset.Now, "mock-version-id"));

        blobRepositoryMock
            .Setup(b =>
                b.DeleteBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        var result = await testController.CreateAndUploadData(
            _instanceOwnerPartyId,
            Guid.NewGuid(),
            _dataType,
            CancellationToken.None
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("Empty stream provided. Cannot persist data.", badRequest.Value);
    }

    [Fact]
    public async Task OverwriteData_NullExistingBlobVersionId_StoresNewBlobVersionId()
    {
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
            "/blobVersionId",
        ];

        (DataController testController, Mock<IDataRepository> dataRepositoryMock, _) =
            GetTestController(
                expectedPropertiesForPatch,
                includeRequestBody: true,
                blobVersionId: null
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
                        p.ContainsKey("/blobVersionId")
                        && (string)p["/blobVersionId"] == "mock-version-id"
                    ),
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

        if (propertyList.Keys.Intersect(_forbiddenUpdateProps).Any())
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
        Action<Mock<IDataService>> configureDataService = null
    )
    {
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
                It.IsAny<CancellationToken>()
            )
        );

        if (throwOnUpdate)
        {
            updateSetup.ThrowsAsync(new InvalidOperationException("metadata update failed"));
        }
        else
        {
            updateSetup.ReturnsAsync(new DataElement());
        }

        var createSetup = dataRepositoryMock.Setup(d =>
            d.Create(It.IsAny<DataElement>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
        );

        if (throwOnCreate)
        {
            createSetup.ThrowsAsync(new InvalidOperationException("metadata create failed"));
        }
        else
        {
            createSetup.ReturnsAsync((DataElement de, long _, CancellationToken _) => de);
        }

        dataRepositoryMock
            .Setup(d => d.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (Guid instanceGuid, Guid dataElementId, CancellationToken cancellationToken) =>
                    new DataElement
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                        DataType = _dataType,
                        IsRead = isRead,
                        ContentType = "application/octet-stream",
                        BlobStoragePath = $"ttd/apps-test/{instanceGuid}/data/{dataElementId}",
                        BlobVersionId = blobVersionId,
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
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));

        blobRepositoryMock
            .Setup(d =>
                d.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((123145864564, DateTimeOffset.Now, "mock-version-id"));

        blobRepositoryMock
            .Setup(d =>
                d.DeleteBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>()
                )
            )
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
                ) =>
                    (
                        new Instance
                        {
                            Id = $"555/{instanceGuid}",
                            InstanceOwner = new InstanceOwner { PartyId = "555" },
                            Org = _org,
                            AppId = _appId,
                            Data = includeDataElements ? GetDataElements(instanceGuid) : null,
                        },
                        0
                    )
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
                It.IsAny<Instance>(),
                It.IsAny<DataType>(),
                It.IsAny<DataElement>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()
            )
        );
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
            authorizationServiceMock.Object,
            NullLogger<DataController>.Instance
        )
        {
            ControllerContext = controllerContext,
        };

        return (sut, dataRepositoryMock, blobRepositoryMock);
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
