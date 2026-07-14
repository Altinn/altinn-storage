#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
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
using HttpMediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;

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
    public async Task CommitMutation_EmptyDataValuesAndPresentationTexts_NormalizesToNullRemovals()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [],
            DataValues = new Dictionary<string, string> { ["removeData"] = "old-data" },
            PresentationTexts = new Dictionary<string, string> { ["removeText"] = "old-text" },
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [],
            InternalId: 123L
        );
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            new Application { Id = _appId, Org = _org },
            mutationJson: null
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(new InstanceMutationApplyResult(false, [], instanceInternal));
        SetJsonMutationRequest(
            fixture.HttpContext,
            """
            {
              "dataValues": {
                "removeData": "",
                "setData": "new-data"
              },
              "presentationTexts": {
                "removeText": "",
                "setText": "new-text"
              }
            }
            """
        );

        // Act
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedMutation);
        Assert.Null(capturedMutation.InstanceUpdates.DataValues["removeData"]);
        Assert.Equal("new-data", capturedMutation.InstanceUpdates.DataValues["setData"]);
        Assert.Null(capturedMutation.InstanceUpdates.PresentationTexts["removeText"]);
        Assert.Equal("new-text", capturedMutation.InstanceUpdates.PresentationTexts["setText"]);
    }

    [Fact]
    public async Task CommitMutation_IdempotentReplay_ReturnsBeforeCurrentDataValidation()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid deletedDataElementId = Guid.NewGuid();
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [],
            InternalId: 123L,
            versions: new StorageVersions(13, 9)
        );
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            new Application
            {
                Id = _appId,
                Org = _org,
                DataTypes = [new DataType { Id = _dataType }],
            },
            mutationJson: null
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.TryReplayAdmission(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, ["replayed-id"], instanceInternal));
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "12";
        fixture.HttpContext.Request.Headers[StorageHeaders.IdempotencyKey] =
            idempotencyKey.ToString();
        SetJsonMutationRequest(
            fixture.HttpContext,
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{deletedDataElementId}}",
                  "tags": ["retry-should-not-validate-current-data"]
                }
              ]
            }
            """
        );

        // Act
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        // Assert
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.NotSame(instance, response.Instance);
        Assert.Equal(instance.Id, response.Instance.Id);
        Assert.NotNull(response.Instance.SelfLinks?.Platform);
        Assert.True(response.Replayed);
        Assert.Equal(
            "13",
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "9",
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_IdempotencyKeyWithMatchingVersion_SkipsReplayAdmissionAndUsesApplySnapshot()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.Parse("22222222-2222-2222-2222-222222222222");
        InstanceMutationCommit capturedMutation = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(7, 3)),
            CreateAggregateApplication(),
            """
            {
              "dataValues": {
                "status": "updated"
              }
            }
            """
        );
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "7";
        fixture.HttpContext.Request.Headers[StorageHeaders.IdempotencyKey] =
            idempotencyKey.ToString("B");
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    fixture.InstanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation, new StorageVersions(8, 3))
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.False(response.Replayed);
        Assert.Equal("updated", response.Instance.DataValues["status"]);
        Assert.Equal("8", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("3", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        Assert.Equal(idempotencyKey, capturedMutation.IdempotencyKey);
        fixture.MutationRepository.Verify(
            repository =>
                repository.TryReplayAdmission(
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        fixture.InstanceRepository.Verify(
            repository => repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_InvalidIdempotencyKey_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "dataValues": {
                "status": "updated"
              }
            }
            """
        );
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "7";
        fixture.HttpContext.Request.Headers[StorageHeaders.IdempotencyKey] = "not-a-guid";

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Idempotency-Key must be a valid GUID.", badRequest.Value);
        fixture.InstanceRepository.Verify(
            repository =>
                repository.GetOne(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_WhenReferencedCreatePartIsMissing_CleansUpEarlierStagedFile()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "firstFile"
                },
                {
                  "dataType": "attachment",
                  "contentPartName": "missingFile"
                }
              ]
            }
            """,
            CreateFormFile("firstFile")
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("No multipart file part named 'missingFile' was supplied.", badRequest.Value);
        InstanceMutationAsserts.VerifyStagedBlobCompensation(
            fixture.DataRepository,
            fixture.BlobRepository
        );
    }

    [Fact]
    public async Task CommitMutation_WhenReferencedUpdatePartIsMissing_CleansUpEarlierStagedFile()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [dataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "createFile"
                }
              ],
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "contentPartName": "missingUpdateFile"
                }
              ]
            }
            """,
            CreateFormFile("createFile")
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "No multipart file part named 'missingUpdateFile' was supplied.",
            badRequest.Value
        );
        InstanceMutationAsserts.VerifyStagedBlobCompensation(
            fixture.DataRepository,
            fixture.BlobRepository
        );
    }

    [Theory]
    [InlineData("dataValues")]
    [InlineData("presentationTexts")]
    public async Task CommitMutation_WhenInstanceFieldAuthorizationFails_DoesNotStagePlannedFile(
        string instanceField
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ],
              "{{instanceField}}": {
                "summary": "ready"
              }
            }
            """,
            CreateFormFile("attachment")
        );
        if (instanceField == "dataValues")
        {
            fixture
                .ProcessAuthorizer.Setup(authorizer =>
                    authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>())
                )
                .ReturnsAsync(false);
        }
        else
        {
            fixture
                .ProcessAuthorizer.Setup(authorizer =>
                    authorizer.AuthorizePresentationTextsUpdate(It.IsAny<InstanceInternal>())
                )
                .ReturnsAsync(false);
        }

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.DataRepository.Verify(
            repository =>
                repository.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenAggregateApplyHasStaleInstanceVersion_CleansUpStagedBlob()
    {
        Guid instanceGuid = Guid.NewGuid();
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "1";
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.IsAny<Guid>(),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InstanceVersionMismatchException(8, 3));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        Assert.Equal("8", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("3", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        allocatedBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
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
    public async Task CommitMutation_WhenAggregateApplyOutcomeIsUnknown_LeavesStagedBlobForOrphanCleanup()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new TimeoutException("commit outcome unknown"));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            fixture.Sut.CommitMutation(555, instanceGuid, CancellationToken.None)
        );

        Assert.Equal("commit outcome unknown", exception.Message);
        InstanceMutationAsserts.VerifyNoStagedBlobCompensation(
            fixture.DataRepository,
            fixture.BlobRepository
        );
    }

    [Fact]
    public async Task CommitMutation_WhenAggregateApplyFailsWithDefiniteRollback_CleansUpStagedBlob()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new RepositoryException("apply failed"));

        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            fixture.Sut.CommitMutation(555, instanceGuid, CancellationToken.None)
        );

        Assert.Equal("apply failed", exception.Message);
        InstanceMutationAsserts.VerifyStagedBlobCompensation(
            fixture.DataRepository,
            fixture.BlobRepository
        );
    }

    [Fact]
    public async Task CommitMutation_WhenCreateThenAggregateApplyHasBlobVersionMismatch_CleansUpAndWritesDurableExceptionVersionHeaders()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string createdBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string updatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = BlobRepository.GetVersionedBlobPath(
                _appId,
                instanceGuid.ToString(),
                currentBlobVersionId
            ),
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(
                instanceGuid,
                [dataElement.FromApiModel(currentBlobVersionId)],
                new StorageVersions(11, 5)
            ),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "created"
                }
              ],
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "contentPartName": "updated",
                  "expectedCurrentBlobVersion": "\"{{BlobVersionId.Encode(
                Guid.CreateVersion7()
            )}}\""
                }
              ]
            }
            """,
            CreateFormFile("created"),
            CreateFormFile("updated")
        );
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    dataElementId,
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(updatedBlobVersionId);
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.Is<Guid>(id => id != dataElementId),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(createdBlobVersionId);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new DataElementBlobVersionMismatchException(
                    "Data element current blob version did not match expected version.",
                    11,
                    5
                )
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        Assert.Equal("11", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("5", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        fixture.InstanceRepository.Verify(
            repository => repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()),
            Times.Never
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        createdBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        updatedBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.Is<Guid>(id => id != dataElementId),
                    createdBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    dataElementId,
                    updatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_WhenAggregateApplyReplays_CleansUpStagedBlobAndReturnsResponse()
    {
        Guid instanceGuid = Guid.NewGuid();
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(12, 4)),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.IsAny<Guid>(),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(true, ["replayed-id"], fixture.InstanceInternal)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.Equal($"555/{fixture.InstanceInternal.Id}", response.Instance.Id);
        Assert.NotNull(response.Instance.SelfLinks?.Platform);
        Assert.Equal(["replayed-id"], response.CreatedDataElementIds);
        Assert.True(response.Replayed);
        Assert.Equal("12", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("4", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        allocatedBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
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
        fixture.DataService.Verify(
            service =>
                service.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenPrincipalHasNoUserOrOrg_ReturnsForbidBeforeWork()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "dataValues": {
                "status": "updated"
              }
            }
            """
        );
        fixture.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity("mock"));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.InstanceRepository.Verify(
            repository =>
                repository.GetOne(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_CreateDataElement_IncludesCreatedEventInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Created,
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal dataElement
                ) => BuildDataElementEvent(eventType, instance, dataElement)
            );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceEvent createdEvent = Assert.Single(capturedMutation.InstanceEvents);
        DataElementInternal createdDataElement = Assert.Single(capturedMutation.CreateDataElements);
        Assert.Equal(InstanceEventType.Created.ToString(), createdEvent.EventType);
        Assert.Equal(createdDataElement.Id.ToString(), createdEvent.DataId);
        Assert.False(string.IsNullOrEmpty(createdDataElement.Id));
        Assert.Null(createdDataElement.LastChanged);
        Assert.Null(createdDataElement.LastChangedBy);
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
    public async Task CommitMutation_CreateDataElement_WithLockedTrue_CreatesLockedDataElement()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid callerSuppliedDataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataElementId": "{{callerSuppliedDataElementId}}",
                  "dataType": "attachment",
                  "contentPartName": "attachment",
                  "locked": true
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementInternal createdDataElement = Assert.Single(capturedMutation.CreateDataElements);
        Assert.NotEqual(callerSuppliedDataElementId.ToString(), createdDataElement.Id);
        Assert.False(string.IsNullOrEmpty(createdDataElement.Id));
        Assert.True(createdDataElement.Locked);
    }

    [Fact]
    public async Task CommitMutation_CreateDataElements_ReturnsGeneratedIdsInRequestOrderAndSnapshot()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid callerSuppliedFirstId = Guid.NewGuid();
        Guid callerSuppliedSecondId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        InstanceInternal updatedInstanceInternal = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataElementId": "{{callerSuppliedFirstId}}",
                  "dataType": "attachment",
                  "contentPartName": "firstFile"
                },
                {
                  "dataElementId": "{{callerSuppliedSecondId}}",
                  "dataType": "attachment",
                  "contentPartName": "secondFile",
                  "locked": true
                }
              ]
            }
            """,
            CreateFormFile("firstFile"),
            CreateFormFile("secondFile")
        );
        fixture
            .InstanceRepository.Setup(repository =>
                repository.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>())
            )
            .Returns(() => Task.FromResult(updatedInstanceInternal ?? fixture.InstanceInternal));
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) =>
                {
                    capturedMutation = mutation;
                    updatedInstanceInternal = CreateAggregateInstanceInternal(
                        instanceGuid,
                        [.. mutation.CreateDataElements],
                        new StorageVersions(2, 1)
                    );
                }
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation, new StorageVersions(2, 1))
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        List<string> createdIds = capturedMutation
            .CreateDataElements.Select(dataElement => dataElement.Id.ToString())
            .ToList();
        Assert.Equal(createdIds, response.CreatedDataElementIds);
        Assert.False(response.Replayed);
        Assert.DoesNotContain(callerSuppliedFirstId.ToString(), createdIds);
        Assert.DoesNotContain(callerSuppliedSecondId.ToString(), createdIds);
        Assert.Equal(2, createdIds.Select(Guid.Parse).Distinct().Count());
        Assert.All(
            createdIds,
            createdId =>
                Assert.Contains(response.Instance.Data, dataElement => dataElement.Id == createdId)
        );
        Assert.False(
            response.Instance.Data.Single(dataElement => dataElement.Id == createdIds[0]).Locked
        );
        Assert.True(
            response.Instance.Data.Single(dataElement => dataElement.Id == createdIds[1]).Locked
        );
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementContent_IncludesSavedEventInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/path",
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [dataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Saved,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId.ToString())
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal eventDataElement
                ) => BuildDataElementEvent(eventType, instance, eventDataElement)
            );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceEvent savedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Saved.ToString(), savedEvent.EventType);
        Assert.Equal(dataElementId.ToString(), savedEvent.DataId);
        InstanceMutationDataElementUpdate updatedDataElement = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.False(updatedDataElement.Properties.ContainsKey("/lastChanged"));
        Assert.False(updatedDataElement.Properties.ContainsKey("/lastChangedBy"));
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
    public async Task CommitMutation_UpdateWriteActionNotAuthorized_ForbidsBeforeReplayAdmission()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };
        Application application = CreateAggregateApplication();
        application.DataTypes[0].ActionRequiredToWrite = "write";
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(
                instanceGuid,
                [existingDataElement.FromApiModel(null)],
                new StorageVersions(13, 9)
            ),
            application,
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "tags": ["blocked-update"]
                }
              ]
            }
            """
        );
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "12";
        fixture.HttpContext.Request.Headers[StorageHeaders.IdempotencyKey] =
            idempotencyKey.ToString();

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.MutationRepository.Verify(
            repository =>
                repository.TryReplayAdmission(
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_ProcessNextNotAuthorized_ForbidsBeforeApply()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "processState": {
                "state": {
                  "currentTask": {
                    "elementId": "Task_2",
                    "altinnTaskType": "data"
                  }
                }
              }
            }
            """
        );
        fixture.InstanceInternal.Process = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1", AltinnTaskType = "data" },
        };
        fixture
            .ProcessAuthorizer.Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(false);

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_ProcessStateOnInstanceWithoutCurrentTask_ReturnsForbidden()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "processState": {
                "state": {
                  "ended": "2025-06-01T12:00:00Z"
                }
              }
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.ProcessAuthorizer.Verify(
            authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                ),
            Times.Never
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_ProcessStateWithoutCurrentTask_IdempotentReplay_StillReplays()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(13, 9)),
            CreateAggregateApplication(),
            """
            {
              "processState": {
                "state": {
                  "ended": "2025-06-01T12:00:00Z"
                }
              }
            }
            """
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.TryReplayAdmission(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, [], fixture.InstanceInternal));
        fixture.HttpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] = "12";
        fixture.HttpContext.Request.Headers[StorageHeaders.IdempotencyKey] =
            idempotencyKey.ToString();

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.True(response.Replayed);
        fixture.ProcessAuthorizer.Verify(
            authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_InstanceEventWithInvalidUser_ReturnsBadRequestBeforeInstanceFetch()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "processState": {
                "events": [
                  {
                    "eventType": "process_StartTask"
                  }
                ]
              }
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<BadRequestObjectResult>(result.Result);
        fixture.InstanceRepository.Verify(
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
    public async Task CommitMutation_InstanceEventWithMismatchedInstanceId_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "processState": {
                "events": [
                  {
                    "eventType": "process_StartTask",
                    "instanceId": "555/{{Guid.NewGuid()}}",
                    "user": {
                      "userId": 1337
                    }
                  }
                ]
              }
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Instance ID in InstanceEvent does not match the Instance ID",
            badRequest.Value
        );
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementUnlockMetadataOnly_IgnoresLockInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            Locked = true,
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [existingDataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "locked": false
                }
              ]
            }
            """
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationDataElementUpdate capturedUpdate = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.True(capturedUpdate.IgnoreLock);
        Assert.Equal(false, capturedUpdate.Properties["/locked"]);
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementMetadataOnly_EnforcesLockInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [existingDataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "tags": ["metadata-change"]
                }
              ]
            }
            """
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationDataElementUpdate capturedUpdate = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.False(capturedUpdate.IgnoreLock);
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementContentWithUnlock_EnforcesLockInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/update-path",
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [existingDataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "contentPartName": "updateFile",
                  "locked": false
                }
              ]
            }
            """,
            CreateFormFile("updateFile")
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationDataElementUpdate capturedUpdate = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.False(capturedUpdate.IgnoreLock);
        Assert.Equal(false, capturedUpdate.Properties["/locked"]);
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementMetadataOnly_LeavesElementStampForRepositoryMutationTimestamp()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            Metadata = [new KeyValueEntry { Key = "existing", Value = "metadata" }],
            LastChanged = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            LastChangedBy = "previous-user",
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [existingDataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            $$"""
            {
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "metadata": [
                    {
                      "key": "changed",
                      "value": "metadata"
                    }
                  ]
                }
              ]
            }
            """
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    InstanceMutationDataElementUpdate update = Assert.Single(
                        mutation.UpdateDataElements
                    );
                    DataElement stampedDataElement = new()
                    {
                        Id = dataElementId.ToString(),
                        InstanceGuid = instanceGuid.ToString(),
                        DataType = _dataType,
                        Metadata = (List<KeyValueEntry>)update.Properties["/metadata"],
                        LastChanged = mutation.LastChanged,
                        LastChangedBy = mutation.LastChangedBy,
                    };
                    InstanceInternal stampedInstance = fixture.InstanceInternal;
                    stampedInstance.Data = [stampedDataElement.FromApiModel(null)];
                    return new InstanceMutationApplyResult(false, [], stampedInstance);
                }
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        InstanceMutationDataElementUpdate updatedDataElement = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        List<KeyValueEntry> metadata = Assert.IsType<List<KeyValueEntry>>(
            updatedDataElement.Properties["/metadata"]
        );
        KeyValueEntry entry = Assert.Single(metadata);
        Assert.Equal("changed", entry.Key);
        Assert.Equal("metadata", entry.Value);
        Assert.False(updatedDataElement.Properties.ContainsKey("/lastChanged"));
        Assert.False(updatedDataElement.Properties.ContainsKey("/lastChangedBy"));
        Assert.NotNull(capturedMutation.LastChanged);
        Assert.Equal("200001", capturedMutation.LastChangedBy);
        DataElement responseDataElement = Assert.Single(response.Instance.Data);
        Assert.Equal(capturedMutation.LastChanged, responseDataElement.LastChanged);
        Assert.Equal(capturedMutation.LastChangedBy, responseDataElement.LastChangedBy);
    }

    [Fact]
    public async Task CommitMutation_DataAndProcessEvents_IncludesAllEventsInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid updateDataElementId = Guid.NewGuid();
        Guid deleteDataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement updateDataElement = new()
        {
            Id = updateDataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/update-path",
        };
        DataElement deleteDataElement = new()
        {
            Id = deleteDataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/delete-path",
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(
                instanceGuid,
                [updateDataElement.FromApiModel(null), deleteDataElement.FromApiModel(null)]
            ),
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "createFile"
                }
              ],
              "updateDataElements": [
                {
                  "dataElementId": "{{updateDataElementId}}",
                  "contentPartName": "updateFile"
                }
              ],
              "deleteDataElements": [
                {
                  "dataElementId": "{{deleteDataElementId}}"
                }
              ],
              "processState": {
                "events": [
                  {
                    "eventType": "process_StartTask",
                    "dataId": "process-event",
                    "user": {
                      "userId": 1337
                    }
                  }
                ]
              }
            }
            """,
            CreateFormFile("createFile"),
            CreateFormFile("updateFile")
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal dataElement
                ) => BuildDataElementEvent(eventType, instance, dataElement)
            );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(fixture.InstanceInternal, mutation)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        string createdDataElementId = Assert
            .Single(capturedMutation.CreateDataElements)
            .Id.ToString();
        Assert.Equal(
            [
                (InstanceEventType.process_StartTask.ToString(), "process-event"),
                (InstanceEventType.Created.ToString(), createdDataElementId),
                (InstanceEventType.Saved.ToString(), updateDataElementId.ToString()),
                (InstanceEventType.Deleted.ToString(), deleteDataElementId.ToString()),
            ],
            capturedMutation
                .InstanceEvents.Select(instanceEvent =>
                    (instanceEvent.EventType, instanceEvent.DataId)
                )
                .ToList()
        );
        Assert.Equal(
            4,
            capturedMutation
                .InstanceEvents.Select(instanceEvent =>
                    (instanceEvent.EventType, instanceEvent.DataId)
                )
                .Distinct()
                .Count()
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
    public async Task CommitMutation_DataElementEventsWithProcessStateUpdate_UseUpdatedProcessContext()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid updateDataElementId = Guid.NewGuid();
        DataElement updateDataElement = new()
        {
            Id = updateDataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/update-path",
        };
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(
            instanceGuid,
            [updateDataElement.FromApiModel(null)]
        );
        instanceInternal.Process = new ProcessState
        {
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_Old",
                AltinnTaskType = "confirmation",
            },
        };
        List<(
            InstanceEventType EventType,
            string DataId,
            string CurrentTaskId,
            string CurrentTaskType
        )> capturedEventContexts = [];
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "createFile"
                }
              ],
              "updateDataElements": [
                {
                  "dataElementId": "{{updateDataElementId}}",
                  "contentPartName": "updateFile"
                }
              ],
              "processState": {
                "state": {
                  "currentTask": {
                    "elementId": "Task_Updated",
                    "altinnTaskType": "data"
                  }
                }
              }
            }
            """,
            CreateFormFile("createFile"),
            CreateFormFile("updateFile")
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Callback<InstanceEventType, InstanceInternal, DataElementInternal>(
                (eventType, eventInstance, dataElement) =>
                    capturedEventContexts.Add(
                        (
                            eventType,
                            dataElement.Id.ToString(),
                            eventInstance.Process?.CurrentTask?.ElementId,
                            eventInstance.Process?.CurrentTask?.AltinnTaskType
                        )
                    )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal dataElement
                ) => BuildDataElementEvent(eventType, instance, dataElement)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Collection(
            capturedEventContexts,
            created =>
            {
                Assert.Equal(InstanceEventType.Created, created.EventType);
                Assert.NotEqual(Guid.Empty, Guid.Parse(created.DataId));
                Assert.Equal("Task_Updated", created.CurrentTaskId);
                Assert.Equal("data", created.CurrentTaskType);
            },
            saved =>
            {
                Assert.Equal(
                    (
                        InstanceEventType.Saved,
                        updateDataElementId.ToString(),
                        "Task_Updated",
                        "data"
                    ),
                    saved
                );
            }
        );
    }

    [Fact]
    public async Task CommitMutation_WhenAggregateApplyFailsAfterStaging_CleansUpAndRunsNoPostCommitWork()
    {
        Guid instanceGuid = Guid.NewGuid();
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.IsAny<Guid>(),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Created,
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal dataElement
                ) => BuildDataElementEvent(eventType, instance, dataElement)
            );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new RepositoryException("event insert failed", HttpStatusCode.Conflict));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        allocatedBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
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
        fixture.DataService.Verify(
            service =>
                service.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenDataElementEventBuildFailsAfterStaging_CleansUpStagedBlob()
    {
        Guid instanceGuid = Guid.NewGuid();
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        int order = 0;
        int buildEventOrder = 0;
        int deleteBlobOrder = 0;
        int deleteBlobVersionOrder = 0;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """,
            CreateFormFile("attachment")
        );
        fixture
            .DataRepository.Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.IsAny<Guid>(),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        fixture
            .BlobRepository.Setup(repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        allocatedBlobVersionId
                    ),
                    7
                )
            )
            .Callback(() => deleteBlobOrder = ++order)
            .ReturnsAsync(true);
        fixture
            .DataRepository.Setup(repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    allocatedBlobVersionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => deleteBlobVersionOrder = ++order)
            .ReturnsAsync(true);
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Created,
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Callback(() => buildEventOrder = ++order)
            .Throws(new InvalidOperationException("event build failed"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                fixture.Sut.CommitMutation(555, instanceGuid, CancellationToken.None)
        );

        Assert.Equal("event build failed", exception.Message);
        Assert.True(buildEventOrder > 0);
        Assert.True(deleteBlobOrder > buildEventOrder);
        Assert.True(deleteBlobVersionOrder > buildEventOrder);
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
        fixture.InstanceEventService.Verify(
            service =>
                service.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                ),
            Times.Never
        );
        fixture.DataService.Verify(
            service =>
                service.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenLaterStagingFails_CleansUpEarlierStagedBlob()
    {
        Guid instanceGuid = Guid.NewGuid();
        string firstBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string secondBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "firstFile"
                },
                {
                  "dataType": "attachment",
                  "contentPartName": "secondFile"
                }
              ]
            }
            """,
            CreateFormFile("firstFile"),
            CreateFormFile("secondFile")
        );
        fixture
            .DataRepository.SetupSequence(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.IsAny<Guid>(),
                    _appId,
                    _org,
                    7,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(firstBlobVersionId)
            .ReturnsAsync(secondBlobVersionId);
        fixture
            .BlobRepository.SetupSequence(repository =>
                repository.WriteBlob(_org, It.IsAny<Stream>(), It.IsAny<string>(), 7)
            )
            .ReturnsAsync((42L, DateTimeOffset.UtcNow))
            .ThrowsAsync(new InvalidOperationException("second blob write failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.CommitMutation(555, instanceGuid, CancellationToken.None)
        );

        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        firstBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        secondBlobVersionId
                    ),
                    7
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    firstBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    secondBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartMutationIsNotFirst_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        const string mutationJson = """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile("attachment")
        );
        SetMultipartMutationRequest(
            fixture.HttpContext,
            mutationJson,
            [CreateFormFile("attachment")],
            mutationFirst: false
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Multipart aggregate mutation requests must start with a 'mutation' JSON field.",
            badRequest.Value
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartContainsSecondMutationField_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """{"createDataElements":[{"dataType":"attachment","contentPartName":"file1"}]}""",
            CreateFormFile("file1")
        );

        string mutationJson =
            """{"createDataElements":[{"dataType":"attachment","contentPartName":"file1"}]}""";
        using MultipartFormDataContent content = new();
        AddMutationPart(content, mutationJson);
        AddMutationPart(content, "{}");
        AddFileParts(content, [CreateFormFile("file1")]);
        SetRequestBody(fixture.HttpContext, content);

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Multipart aggregate mutation requests must contain only one 'mutation' field.",
            badRequest.Value
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartHasNoMutationSection_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            "{}",
            CreateFormFile("attachment")
        );
        using MultipartFormDataContent content = new();
        AddFileParts(content, [CreateFormFile("attachment")]);
        SetRequestBody(fixture.HttpContext, content);

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Multipart aggregate mutation requests must start with a 'mutation' JSON field.",
            badRequest.Value
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartHasUnknownPart_CleansUpAndReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        const string mutationJson = """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "knownFile"
                }
              ]
            }
            """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile("knownFile")
        );
        SetMultipartMutationRequest(
            fixture.HttpContext,
            mutationJson,
            [CreateFormFile("knownFile")],
            mutationFirst: true,
            extraParts: [CreateFormFile("unknownFile")]
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Unexpected multipart part 'unknownFile'.", badRequest.Value);
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartHasDuplicatePart_CleansUpAndReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        const string mutationJson = """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile("attachment")
        );
        SetMultipartMutationRequest(
            fixture.HttpContext,
            mutationJson,
            [CreateFormFile("attachment")],
            mutationFirst: true,
            extraParts: [CreateFormFile("attachment")]
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Multipart file part name 'attachment' was supplied more than once.",
            badRequest.Value
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_WhenContentPartNameIsSharedAcrossOperations_ReturnsBadRequestWithoutStaging()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "sharedPart"
                },
                {
                  "dataType": "attachment",
                  "contentPartName": "sharedPart"
                }
              ]
            }
            """,
            CreateFormFile("sharedPart")
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "contentPartName 'sharedPart' is referenced by more than one operation.",
            badRequest.Value
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartFileIsEmpty_ReturnsUnprocessableAndDeletesAllocation()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "emptyFile"
                }
              ]
            }
            """,
            CreateFormFile("emptyFile", content: string.Empty)
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        UnprocessableEntityObjectResult unprocessable =
            Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal("Could not process attached file", unprocessable.Value);
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
        fixture.DataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_WhenJsonRequestReferencesFileParts_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        const string mutationJson = """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ]
            }
            """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile("attachment")
        );
        fixture.HttpContext.Request.ContentType = "application/json";
        fixture.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(mutationJson));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("File parts require a multipart/form-data request.", badRequest.Value);
    }

    [Fact]
    public async Task CommitMutation_WhenJsonBodyTooLarge_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            "{}",
            []
        );

        string oversizedJson = new('x', 4 * 1024 * 1024 + 1);
        fixture.HttpContext.Request.ContentType = "application/json";
        fixture.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(oversizedJson));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Mutation JSON exceeds maximum allowed size.", badRequest.Value);
    }

    [Fact]
    public async Task CommitMutation_WhenJsonBodyHasTrailingContent_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            "{}",
            []
        );

        const string jsonWithTrailingContent = """{"dataValues":{"key":"value"}} trailing""";
        fixture.HttpContext.Request.ContentType = "application/json";
        fixture.HttpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(jsonWithTrailingContent)
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.StartsWith(
            "Unable to parse mutation request JSON:",
            Assert.IsType<string>(badRequest.Value)
        );
    }

    [Fact]
    public async Task CommitMutation_WhenWirePartOrderDiffersFromPlanOrder_ReturnsOk()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        const string createPartName = "firstCreate";
        const string updatePartName = "secondUpdate";
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "existing/path",
        };
        string mutationJson = $$"""
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "{{createPartName}}"
                }
              ],
              "updateDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "contentPartName": "{{updatePartName}}"
                }
              ]
            }
            """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [dataElement.FromApiModel(null)]),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile(createPartName),
            CreateFormFile(updatePartName)
        );
        SetMultipartMutationRequest(
            fixture.HttpContext,
            mutationJson,
            [CreateFormFile(updatePartName), CreateFormFile(createPartName)],
            mutationFirst: true
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        fixture.BlobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CommitMutation_WhenMultipartMutationJsonIsTooLarge_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            "{}",
            []
        );
        string oversizedMutationJson = "{" + new string(' ', 5 * 1024 * 1024) + "}";
        SetMultipartMutationRequest(fixture.HttpContext, oversizedMutationJson, []);

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Mutation JSON exceeds maximum allowed size.", badRequest.Value);
    }

    [Fact]
    public async Task CommitMutation_DeleteDataElement_IncludesDeletedEventInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        bool postCommitDeletedEventDispatched = false;
        bool postCommitBlobCleanupRan = false;
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            LastChangedBy = "previous-user",
        };
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [dataElement],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [dataElement.FromApiModel(null)],
            InternalId: 123L,
            versions: new StorageVersions(4, 2)
        );
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            new Application
            {
                Id = _appId,
                Org = _org,
                DataTypes = [new DataType { Id = _dataType }],
            },
            mutationJson: null
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(instanceInternal, mutation)
            );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId.ToString())
                )
            )
            .Returns(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = dataElementId.ToString(),
                }
            );
        fixture
            .InstanceEventService.Setup(service =>
                service.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId.ToString())
                )
            )
            .Callback(() => postCommitDeletedEventDispatched = true)
            .Returns(Task.CompletedTask);
        fixture
            .DataService.Setup(service =>
                service.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => postCommitBlobCleanupRan = true)
            .Returns(Task.CompletedTask);
        SetJsonMutationRequest(
            fixture.HttpContext,
            $$"""
            {
              "deleteDataElements": [
                {
                  "dataElementId": "{{dataElementId}}"
                }
              ]
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationDataElementDelete capturedDelete = Assert.Single(
            capturedMutation.DeleteDataElements
        );
        Assert.Equal(dataElementId.ToString(), capturedDelete.DataElement.Id);
        Assert.False(capturedDelete.IgnoreLock);
        bool hasTransactionalDeletedEvent =
            capturedMutation?.InstanceEvents?.Any(e =>
                e.EventType == InstanceEventType.Deleted.ToString()
                && e.DataId == dataElementId.ToString()
            ) == true;
        Assert.True(
            hasTransactionalDeletedEvent && !postCommitDeletedEventDispatched,
            "Deleted data-element events should be included in the aggregate mutation and should not be dispatched after the aggregate commit."
        );
        Assert.True(
            postCommitBlobCleanupRan,
            "Deleted data-element blob cleanup should still run after a successful aggregate commit."
        );
    }

    [Fact]
    public async Task CommitMutation_DeleteDataElementIgnoreLock_DisablesLockCheckInAggregateMutation()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            Locked = true,
        };
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [dataElement],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [dataElement.FromApiModel(null)],
            InternalId: 123L,
            versions: new StorageVersions(4, 2)
        );
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            new Application
            {
                Id = _appId,
                Org = _org,
                DataTypes = [new DataType { Id = _dataType }],
            },
            mutationJson: null
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId.ToString())
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Deleted.ToString() });
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(instanceInternal, mutation)
            );
        SetJsonMutationRequest(
            fixture.HttpContext,
            $$"""
            {
              "deleteDataElements": [
                {
                  "dataElementId": "{{dataElementId}}",
                  "ignoreLock": true
                }
              ]
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationDataElementDelete capturedDelete = Assert.Single(
            capturedMutation.DeleteDataElements
        );
        Assert.Equal(dataElementId.ToString(), capturedDelete.DataElement.Id);
        Assert.True(capturedDelete.IgnoreLock);
    }

    [Fact]
    public async Task CommitMutation_DeleteDataElement_WhenAggregateApplyFails_DoesNotRunPostCommitWork()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [dataElement],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [dataElement.FromApiModel(null)],
            InternalId: 123L,
            versions: new StorageVersions(4, 2)
        );
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            new Application
            {
                Id = _appId,
                Org = _org,
                DataTypes = [new DataType { Id = _dataType }],
            },
            mutationJson: null
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    123L,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new RepositoryException("aggregate mutation failed", HttpStatusCode.Conflict)
            );
        SetJsonMutationRequest(
            fixture.HttpContext,
            $$"""
            {
              "deleteDataElements": [
                {
                  "dataElementId": "{{dataElementId}}"
                }
              ]
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        fixture.InstanceEventService.Verify(
            service =>
                service.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
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
    public async Task OverwriteData_WithoutIfMatch_UsesReadBlobVersionAsExpectedCurrentBlobVersion()
    {
        // Arrange
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
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

    [Fact]
    public async Task OverwriteData_StartFileScanThrows_DoesNotWriteVersionHeadersOrETag()
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
            "/blobStoragePath",
            "/currentBlobVersion",
        ];

        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7()),
            configureDataService: mock =>
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
                    .ThrowsAsync(new InvalidOperationException("file scan failed"))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.OverwriteData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            )
        );

        Assert.False(testController.Response.Headers.ContainsKey(HeaderNames.ETag));
        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
    }

    [Fact]
    public async Task OverwriteData_DispatchEventThrows_DoesNotWriteVersionHeadersOrETag()
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
            "/blobStoragePath",
            "/currentBlobVersion",
        ];

        (DataController testController, _, _) = GetTestController(
            expectedPropertiesForPatch,
            includeRequestBody: true,
            blobVersionId: BlobVersionId.Encode(Guid.CreateVersion7()),
            configureInstanceEventService: mock =>
                mock.Setup(e =>
                        e.DispatchEvent(
                            InstanceEventType.Saved,
                            It.IsAny<InstanceInternal>(),
                            It.IsAny<DataElementInternal>()
                        )
                    )
                    .ThrowsAsync(new InvalidOperationException("event dispatch failed"))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.OverwriteData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            )
        );

        Assert.False(testController.Response.Headers.ContainsKey(HeaderNames.ETag));
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
    public async Task CreateAndUploadData_StartFileScanThrows_DoesNotWriteVersionHeaders()
    {
        (DataController testController, _, _) = GetTestController(
            [],
            includeRequestBody: true,
            configureDataService: mock =>
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
                    .ThrowsAsync(new InvalidOperationException("file scan failed"))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.CreateAndUploadData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                _dataType,
                CancellationToken.None
            )
        );

        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
        );
    }

    [Fact]
    public async Task CreateAndUploadData_DispatchEventThrows_DoesNotWriteVersionHeaders()
    {
        (DataController testController, _, _) = GetTestController(
            [],
            includeRequestBody: true,
            configureInstanceEventService: mock =>
                mock.Setup(e =>
                        e.DispatchEvent(
                            InstanceEventType.Created,
                            It.IsAny<InstanceInternal>(),
                            It.IsAny<DataElementInternal>()
                        )
                    )
                    .ThrowsAsync(new InvalidOperationException("event dispatch failed"))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testController.CreateAndUploadData(
                _instanceOwnerPartyId,
                Guid.NewGuid(),
                _dataType,
                CancellationToken.None
            )
        );

        Assert.False(testController.Response.Headers.ContainsKey(StorageHeaders.InstanceVersion));
        Assert.False(
            testController.Response.Headers.ContainsKey(StorageHeaders.ProcessStateVersion)
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
    public async Task OverwriteData_UpdateMetadataConflictWithIfMatch_ReturnsPreconditionFailedAndDeletesExplicitVersionBlob()
    {
        // Arrange
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string currentBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string ifMatchBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
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
        string allocatedBlobVersionId = null,
        HeaderDictionary requestHeaders = null,
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

        var createSetup = dataRepositoryMock.Setup(d =>
            d.Create(
                It.IsAny<DataElementInternal>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>(),
                null,
                null
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
                    string blobStoragePath =
                        blobStoragePathOverride
                        ?? (
                            string.IsNullOrEmpty(blobVersionId)
                                ? legacyBlobStoragePath
                                : BlobRepository.GetVersionedBlobPath(
                                    "ttd/apps-test",
                                    instanceGuid.ToString(),
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
                    string instanceGuid = instanceInternal.Id;
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
                            instanceGuid,
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
            .ReturnsAsync(true);

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
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(a => a.AuthorizePresentationTextsUpdate(It.IsAny<InstanceInternal>()))
            .ReturnsAsync(true);
        processAuthorizerMock
            .Setup(a => a.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>()))
            .ReturnsAsync(true);

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
            authorizationServiceMock.Object,
            processAuthorizerMock.Object
        )
        {
            ControllerContext = controllerContext,
        };

        return (sut, dataRepositoryMock, blobRepositoryMock);
    }

    private AggregateMutationFixture CreateAggregateMutationFixture(
        Guid instanceGuid,
        InstanceInternal instanceInternal,
        Application application,
        string mutationJson,
        params IFormFile[] fileParts
    )
    {
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IDataService> dataServiceMock = new();
        Mock<IInstanceEventService> instanceEventServiceMock = new();
        Mock<IAuthorization> authorizationServiceMock = new();
        Mock<IProcessAuthorizer> processAuthorizerMock = new();

        dataRepositoryMock
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
            .ReturnsAsync(BlobVersionId.Encode(Guid.CreateVersion7()));
        dataRepositoryMock
            .Setup(repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        blobRepositoryMock
            .Setup(repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .Returns<string, Stream, string, int?>(
                (org, stream, path, accountNumber) =>
                {
                    using MemoryStream memoryStream = new();
                    stream.CopyTo(memoryStream);
                    long length = memoryStream.Length;
                    return Task.FromResult((length, DateTimeOffset.UtcNow));
                }
            );
        blobRepositoryMock
            .Setup(repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>())
            )
            .ReturnsAsync(true);
        dataServiceMock
            .Setup(service =>
                service.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);
        dataServiceMock
            .Setup(service =>
                service.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(instanceInternal);
        applicationRepositoryMock
            .Setup(repository => repository.FindOne(_appId, _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    instanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    CreateApplyResult(instanceInternal, mutation)
            );
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizePresentationTextsUpdate(It.IsAny<InstanceInternal>())
            )
            .ReturnsAsync(true);
        processAuthorizerMock
            .Setup(authorizer => authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>()))
            .ReturnsAsync(true);
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(200001, 1337) };
        if (mutationJson is not null)
        {
            SetMultipartMutationRequest(httpContext, mutationJson, fileParts);
        }

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
            authorizationServiceMock.Object,
            processAuthorizerMock.Object
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        return new AggregateMutationFixture(
            sut,
            httpContext,
            instanceInternal,
            dataRepositoryMock,
            blobRepositoryMock,
            instanceRepositoryMock,
            dataServiceMock,
            mutationRepositoryMock,
            instanceEventServiceMock,
            processAuthorizerMock
        );
    }

    private static InstanceEvent BuildDataElementEvent(
        InstanceEventType eventType,
        InstanceInternal instance,
        DataElementInternal dataElement
    ) =>
        new()
        {
            EventType = eventType.ToString(),
            InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
            DataId = dataElement.Id.ToString(),
            InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
            ProcessInfo = instance.Process,
        };

    private InstanceInternal CreateAggregateInstanceInternal(
        Guid instanceGuid,
        List<DataElementInternal> dataElements,
        StorageVersions versions = null
    )
    {
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = dataElements.Select(dataElement => dataElement.ToApiModel()).ToList(),
        };

        return InstanceInternalTestFactory.Create(
            instance,
            dataElements,
            InternalId: 123L,
            versions: versions
        );
    }

    private static InstanceMutationApplyResult CreateApplyResult(
        InstanceInternal instanceInternal,
        InstanceMutationCommit mutation,
        StorageVersions versions = null
    )
    {
        List<DataElementInternal> dataElements = [.. instanceInternal.Data];
        foreach (
            InstanceMutationDataElementDelete deleteDataElement in mutation.DeleteDataElements ?? []
        )
        {
            dataElements.RemoveAll(dataElement =>
                dataElement.Id == deleteDataElement.DataElement.Id
            );
        }

        dataElements.AddRange(mutation.CreateDataElements ?? []);

        InstanceInternal instance = instanceInternal;
        if (mutation.InstanceUpdates?.Status is not null)
        {
            instance.Status = mutation.InstanceUpdates.Status;
        }

        if (mutation.InstanceUpdates?.Process is not null)
        {
            instance.Process = mutation.InstanceUpdates.Process;
        }

        if (mutation.InstanceUpdates?.DataValues is not null)
        {
            instance.DataValues = mutation.InstanceUpdates.DataValues;
        }

        if (mutation.InstanceUpdates?.PresentationTexts is not null)
        {
            instance.PresentationTexts = mutation.InstanceUpdates.PresentationTexts;
        }

        instance.Data = dataElements;
        instance.Versions = versions ?? instanceInternal.Versions;

        return new InstanceMutationApplyResult(
            false,
            [
                .. (mutation.CreateDataElements ?? []).Select(dataElement =>
                    dataElement.Id.ToString()
                ),
            ],
            instance
        );
    }

    private Application CreateAggregateApplication() =>
        new()
        {
            Id = _appId,
            Org = _org,
            StorageAccountNumber = 7,
            DataTypes = [new DataType { Id = _dataType }],
        };

    private static IFormFile CreateFormFile(
        string name,
        string fileName = "attachment.txt",
        string content = "file-content",
        string contentType = "text/plain"
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        FormFile file = new(new MemoryStream(bytes), 0, bytes.Length, name, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

        return file;
    }

    private static void SetMultipartMutationRequest(
        DefaultHttpContext httpContext,
        string mutationJson,
        IReadOnlyList<IFormFile> fileParts
    )
    {
        SetMultipartMutationRequest(httpContext, mutationJson, fileParts, mutationFirst: true);
    }

    private static void SetMultipartMutationRequest(
        DefaultHttpContext httpContext,
        string mutationJson,
        IReadOnlyList<IFormFile> fileParts,
        bool mutationFirst,
        IReadOnlyList<IFormFile> extraParts = null
    )
    {
        using MultipartFormDataContent content = new();

        if (mutationFirst)
        {
            AddMutationPart(content, mutationJson);
        }

        AddFileParts(content, fileParts);

        if (!mutationFirst)
        {
            AddMutationPart(content, mutationJson);
        }

        AddFileParts(content, extraParts ?? []);

        SetRequestBody(httpContext, content);
    }

    private static void SetJsonMutationRequest(DefaultHttpContext httpContext, string mutationJson)
    {
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(mutationJson));
    }

    private static void SetRequestBody(DefaultHttpContext httpContext, HttpContent content)
    {
        MemoryStream bodyStream = new();
        content.CopyTo(bodyStream, context: null, CancellationToken.None);
        bodyStream.Position = 0;
        httpContext.Request.Body = bodyStream;
        httpContext.Request.ContentType = content.Headers.ContentType!.ToString();
    }

    private static void AddMutationPart(MultipartFormDataContent content, string mutationJson)
    {
        content.Add(new StringContent(mutationJson, Encoding.UTF8, "application/json"), "mutation");
    }

    private static void AddFileParts(
        MultipartFormDataContent content,
        IReadOnlyList<IFormFile> fileParts
    )
    {
        foreach (IFormFile filePart in fileParts)
        {
            byte[] bytes;
            using (MemoryStream memoryStream = new())
            {
                filePart.OpenReadStream().CopyTo(memoryStream);
                bytes = memoryStream.ToArray();
            }

            ByteArrayContent fileContent = new(bytes);
            fileContent.Headers.ContentType = new HttpMediaTypeHeaderValue(filePart.ContentType);
            content.Add(fileContent, filePart.Name, filePart.FileName);
        }
    }

    private sealed record AggregateMutationFixture(
        DataController Sut,
        DefaultHttpContext HttpContext,
        InstanceInternal InstanceInternal,
        Mock<IDataRepository> DataRepository,
        Mock<IBlobRepository> BlobRepository,
        Mock<IInstanceRepository> InstanceRepository,
        Mock<IDataService> DataService,
        Mock<IInstanceMutationRepository> MutationRepository,
        Mock<IInstanceEventService> InstanceEventService,
        Mock<IProcessAuthorizer> ProcessAuthorizer
    );

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

        return InstanceInternalTestFactory.Create(instance, dataElements, InternalId: 0);
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

internal static class InstanceMutationAsserts
{
    public static void VerifyApplyNever(Mock<IInstanceMutationRepository> mutationRepository) =>
        mutationRepository.Verify(
            repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );

    public static void VerifyStagedBlobCompensation(
        Mock<IDataRepository> dataRepository,
        Mock<IBlobRepository> blobRepository
    )
    {
        dataRepository.Verify(
            repository =>
                repository.CreateBlobVersionId(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        blobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
        dataRepository.Verify(
            repository =>
                repository.DeleteBlobVersion(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Once
        );
    }

    public static void VerifyNoStagedBlobCompensation(
        Mock<IDataRepository> dataRepository,
        Mock<IBlobRepository> blobRepository
    )
    {
        blobRepository.Verify(
            repository =>
                repository.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                ),
            Times.Once
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
        blobRepository.Verify(
            repository =>
                repository.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never
        );
    }
}
