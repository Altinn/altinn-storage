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
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using HttpMediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class InstanceMutationsControllerUnitTests
{
    private readonly string _org = "ttd";
    private readonly string _appId = "ttd/apps-test";
    private readonly string _dataType = "attachment";

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task CommitMutation_UndeclaredDataType_ReturnsBadRequestBeforeStaging(
        string operation
    )
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DataElementInternal existingDataElement = new()
        {
            Id = dataElementId,
            InstanceGuid = instanceGuid,
            DataType = "undeclared",
        };
        string mutationJson = operation switch
        {
            "create" => """
                {
                  "createDataElements": [
                    {
                      "dataType": "undeclared",
                      "contentPartName": "file"
                    }
                  ]
                }
                """,
            "update" => $$"""
                {
                  "updateDataElements": [
                    {
                      "dataElementId": "{{dataElementId}}",
                      "contentPartName": "file"
                    }
                  ]
                }
                """,
            "delete" => $$"""
                {
                  "deleteDataElements": [
                    {
                      "dataElementId": "{{dataElementId}}"
                    }
                  ]
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [existingDataElement]),
            CreateAggregateApplication(),
            mutationJson,
            CreateFormFile("file")
        );

        // Act
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        // Assert
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "Requested element type is not declared in application metadata",
            badRequest.Value
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
        fixture.DataRepository.VerifyNoOtherCalls();
        fixture.BlobRepository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task CommitMutation_RepeatedEmptyDataElementIds_ReturnsRequiredIdError(
        string operation
    )
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            $$"""
            {
              "{{operation}}DataElements": [
                { "dataElementId": "{{Guid.Empty}}" },
                { "dataElementId": "{{Guid.Empty}}" }
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
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"{operation}DataElements[].dataElementId is required.", badRequest.Value);
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
        fixture.DataRepository.VerifyNoOtherCalls();
        fixture.BlobRepository.VerifyNoOtherCalls();
    }

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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation,
            _ => new InstanceMutationApplyResult(false, [], instanceInternal)
        );
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
        Assert.Null(capturedMutation.InstanceChanges.DataValues["removeData"]);
        Assert.Equal("new-data", capturedMutation.InstanceChanges.DataValues["setData"]);
        Assert.Null(capturedMutation.InstanceChanges.PresentationTexts["removeText"]);
        Assert.Equal("new-text", capturedMutation.InstanceChanges.PresentationTexts["setText"]);
    }

    [Fact]
    public async Task CommitMutation_IdempotentReplay_ReturnsBeforeCurrentDataValidation()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid deletedDataElementId = Guid.NewGuid();
        string replayedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        DataElement replayedDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        };
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [replayedDataElement],
        };
        InstanceInternal instanceInternal = InstanceInternalTestFactory.Create(
            instance,
            [replayedDataElement.FromApiModel(replayedBlobVersionId)],
            InternalId: 123L,
            versions: new StorageVersions(13, 9)
        );
        instanceInternal.Process = new ProcessState { Status = ProcessStatus.Processing };
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
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, ["replayed-id"], instanceInternal));
        SetJsonMutationRequest(
            fixture.HttpContext,
            $$"""
            {
              "expectedProcessStatus": "idle",
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
            CancellationToken.None,
            ifInstanceVersionMatch: "12",
            idempotencyKeyHeader: idempotencyKey.ToString()
        );

        // Assert
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.NotSame(instance, response.Instance);
        Assert.Equal(instance.Id, response.Instance.Id);
        Assert.NotNull(response.Instance.SelfLinks?.Platform);
        Assert.True(response.Replayed);
        Assert.Equal(replayedBlobVersionId, Assert.Single(response.Instance.Data).BlobVersionId);
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
    public async Task CommitMutation_IdempotencyKeyWithMatchingVersion_SkipsGetReplayResultAndUsesApplySnapshot()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation,
            mutation =>
                CreateApplyResult(fixture.InstanceInternal, mutation, new StorageVersions(8, 3))
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "7",
            idempotencyKeyHeader: idempotencyKey.ToString("B")
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
                repository.GetReplayResult(
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

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 7)]
    [InlineData(true, null)]
    [InlineData(true, 7)]
    public async Task CommitMutation_WriteUsesSnapshotProcessStateVersionAndPreservesClientInstanceVersion(
        bool includesProcessState,
        int? expectedInstanceVersion
    )
    {
        const int instanceVersion = 7;
        const int processStateVersion = 3;
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        InstanceInternal snapshot = CreateAggregateInstanceInternal(
            instanceGuid,
            [],
            new StorageVersions(instanceVersion, processStateVersion)
        );
        if (includesProcessState)
        {
            snapshot.Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            };
        }

        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            snapshot,
            CreateAggregateApplication(),
            includesProcessState
                ? """
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
                : """
                {
                  "dataValues": {
                    "status": "updated"
                  }
                }
                """
        );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: expectedInstanceVersion?.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedMutation);
        Assert.Equal(expectedInstanceVersion, capturedMutation.ExpectedInstanceVersion);
        Assert.Equal(processStateVersion, capturedMutation.ExpectedProcessStateVersion);
    }

    [Theory]
    [InlineData(
        """{"processState":{"state":{"status":"processing","currentTask":{"elementId":"Task_2"}}}}""",
        ProcessStatus.Idle,
        ProcessStatus.Processing
    )]
    [InlineData(
        """{"expectedProcessStatus":"processing","processState":{"state":{"status":"idle","currentTask":{"elementId":"Task_2"}}}}""",
        ProcessStatus.Processing,
        ProcessStatus.Idle
    )]
    public async Task CommitMutation_ProcessStatus_IsCarriedInProcessPayload(
        string mutationJson,
        ProcessStatus currentProcessStatus,
        ProcessStatus payloadProcessStatus
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(7, 3)),
            CreateAggregateApplication(),
            mutationJson
        );
        fixture.InstanceInternal.Process = new ProcessState
        {
            Status = currentProcessStatus,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1" },
        };
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifProcessStateVersionMatch: "3"
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedMutation);
        Assert.Equal(payloadProcessStatus, capturedMutation.InstanceChanges.Process.Status);
        Assert.Equal(3, capturedMutation.ExpectedProcessStateVersion);
    }

    [Fact]
    public async Task CommitMutation_ProcessingPayloadWithStaleProcessStateVersionFence_ReturnsPreconditionFailed()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(7, 3)),
            CreateAggregateApplication(),
            """{"processState":{"state":{"status":"processing","currentTask":{"elementId":"Task_2"}}}}"""
        );
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifProcessStateVersionMatch: "2"
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(preconditionFailed.Value);
        Assert.Equal("process_state_version_mismatch", problem.Type);
        Assert.Equal(
            "7",
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "3",
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Theory]
    [InlineData("""{"expectedProcessStatus":"future","dataValues":{"value":"update"}}""")]
    [InlineData("""{"processState":{"state":{"status":"future"}}}""")]
    public async Task CommitMutation_UnsupportedTransitionStatus_ReturnsBadRequestBeforeReads(
        string mutationJson
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Unable to parse mutation request JSON", badRequest.Value.ToString());
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
    public async Task CommitMutation_ProcessingPayloadWithoutClientProcessStateVersion_AppliesWithSnapshotFence()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(7, 3)),
            CreateAggregateApplication(),
            """{"processState":{"state":{"status":"processing","currentTask":{"elementId":"Task_2"}}}}"""
        );
        fixture.InstanceInternal.Process = new ProcessState
        {
            Status = ProcessStatus.Idle,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1" },
        };
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation,
            mutation =>
                CreateApplyResult(fixture.InstanceInternal, mutation, new StorageVersions(8, 4))
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.Equal(ProcessStatus.Processing, response.Instance.Process.Status);
        Assert.Equal("8", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("4", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        Assert.NotNull(capturedMutation);
        Assert.Equal(3, capturedMutation.ExpectedProcessStateVersion);
        Assert.Equal(ProcessStatus.Processing, capturedMutation.InstanceChanges.Process.Status);
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
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "7",
            idempotencyKeyHeader: "not-a-guid"
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
            CancellationToken.None,
            ifInstanceVersionMatch: "1"
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        Assert.Equal("8", fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion]);
        Assert.Equal("3", fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion]);
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
            BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
                _appId,
                instanceGuid,
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
                  "expectedCurrentBlobVersion": "{{BlobVersionId.Encode(Guid.CreateVersion7())}}"
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
    public async Task CommitMutation_DeleteInstanceHard_WhenDeletePolicyFails_ReturnsForbid()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "deleteInstance": {
                "hard": true
              }
            }
            """
        );
        fixture
            .PolicyAuthorizationService.Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Failed());

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                ),
            Times.Once
        );
        fixture.InstanceRepository.Verify(
            repository => repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()),
            Times.Once
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
    public async Task CommitMutation_DeleteInstanceHard_WhenApplicationPreventsDeletion_ReturnsForbiddenResponse()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        instanceInternal.Status = new InstanceStatus { Archived = DateTime.UtcNow };
        Application application = CreateAggregateApplication();
        application.PreventInstanceDeletionForDays = 1;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            application,
            """
            {
              "deleteInstance": {
                "hard": true
              }
            }
            """
        );
        fixture
            .PolicyAuthorizationService.Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Success());

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(
            "Instance cannot be deleted yet due to application restrictions.",
            forbidden.Value
        );
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                ),
            Times.Once
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_DeleteInstanceHard_WhenInstanceIsMissing_ReturnsNotFoundBeforeDeleteAuthorization()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "deleteInstance": {
                "hard": true
              }
            }
            """
        );
        fixture
            .InstanceRepository.Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((InstanceInternal)null);
        fixture
            .PolicyAuthorizationService.Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Failed());

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal($"Unable to find any instance with id: 555/{instanceGuid}.", notFound.Value);
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object>(),
                    AuthzConstants.POLICY_INSTANCE_DELETE
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
    public async Task CommitMutation_DeleteInstanceTerminalWorkflowCommit_WhenHardIsFalse_ReturnsBadRequestBeforeInstanceFetch()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "expectedProcessStatus": "processing",
              "processState": {
                "state": {
                  "status": "idle",
                  "ended": "2026-06-07T08:09:10Z",
                  "endEvent": "EndEvent_1"
                }
              },
              "deleteInstance": {
                "hard": false
              }
            }
            """
        );
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "12",
            ifProcessStateVersionMatch: "8",
            idempotencyKeyHeader: Guid.NewGuid().ToString()
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("deleteInstance.hard must be true.", badRequest.Value);
        fixture.InstanceRepository.Verify(
            repository =>
                repository.GetOne(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
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

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, ProcessStatus.Idle)]
    public async Task CommitMutation_DeleteInstanceHard_AppliesStatusMarkerAndDeletedEvent(
        bool hasCurrentStatus,
        ProcessStatus? expectedProcessStatus
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        DateTime archived = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime previousSoftDeleted = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        Altinn.Platform.Storage.Interface.Models.Substatus substatus = new()
        {
            Label = "preserved-label",
            Description = "preserved-description",
        };
        InstanceMutationCommit capturedMutation = null;
        InstanceMutationApplyResult capturedApplyResult = null;
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        InstanceStatus originalStatus = hasCurrentStatus
            ? new InstanceStatus
            {
                IsArchived = true,
                Archived = archived,
                IsSoftDeleted = true,
                SoftDeleted = previousSoftDeleted,
                ReadStatus = ReadStatus.UpdatedSinceLastReview,
                Substatus = substatus,
            }
            : null;
        instanceInternal.Status = originalStatus;
        string mutationJson = expectedProcessStatus is null
            ? """
                {
                  "deleteInstance": {
                    "hard": true
                  }
                }
                """
            : $$"""
                {
                  "expectedProcessStatus": {{JsonSerializer.Serialize(expectedProcessStatus)}},
                  "deleteInstance": {
                    "hard": true
                  }
                }
                """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            mutationJson
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(InstanceEventType.Deleted, It.IsAny<InstanceInternal>())
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal instance) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        InstanceId = instance.Id.ToString(),
                        InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                    }
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
                    capturedApplyResult = CreateApplyResult(fixture.InstanceInternal, mutation);
                    return capturedApplyResult;
                }
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.True(response.Instance.Status.IsHardDeleted);
        Assert.True(response.Instance.Status.IsSoftDeleted);
        Assert.NotNull(response.Instance.Status.HardDeleted);
        Assert.Equal(
            hasCurrentStatus ? previousSoftDeleted : response.Instance.Status.HardDeleted,
            response.Instance.Status.SoftDeleted
        );

        Assert.NotNull(capturedMutation);
        Assert.NotNull(capturedApplyResult);
        if (hasCurrentStatus)
        {
            Assert.Same(originalStatus, instanceInternal.Status);
            Assert.False(originalStatus.IsHardDeleted);
            Assert.Null(originalStatus.HardDeleted);
            Assert.NotSame(originalStatus, capturedApplyResult.Instance.Status);
            Assert.True(response.Instance.Status.IsArchived);
            Assert.Equal(archived, response.Instance.Status.Archived);
            Assert.Equal(ReadStatus.UpdatedSinceLastReview, response.Instance.Status.ReadStatus);
            Assert.Equal(substatus.Label, response.Instance.Status.Substatus.Label);
            Assert.Equal("preserved-label", response.Instance.Status.Substatus.Label);
            Assert.Equal("preserved-description", response.Instance.Status.Substatus.Description);
            Assert.Equal(previousSoftDeleted, response.Instance.Status.SoftDeleted);
        }

        Assert.NotNull(capturedMutation.InstanceChanges.Status);
        Assert.True(capturedMutation.InstanceChanges.Status.HardDeleted.IsSpecified);
        Assert.Equal(DateTimeKind.Utc, capturedMutation.Stamp.LastChanged.Kind);
        Assert.Equal("200001", capturedMutation.Stamp.LastChangedBy);
        Assert.True(capturedMutation.InstanceChanges.Status.IsHardDeleted);
        Assert.True(capturedMutation.InstanceChanges.Status.IsSoftDeleted);
        InstanceEvent deletedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Deleted.ToString(), deletedEvent.EventType);
        Assert.Empty(capturedMutation.CreateDataElements);
        Assert.Empty(capturedMutation.UpdateDataElements);
        Assert.Empty(capturedMutation.DeleteDataElements);
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CommitMutation_DeleteInstanceHard_WithExpectedProcessingButNoTerminalState_ReturnsBadRequest()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        instanceInternal.Process = new ProcessState { Status = ProcessStatus.Processing };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            """
            {
              "expectedProcessStatus": "processing",
              "deleteInstance": {
                "hard": true
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
            "deleteInstance cannot be combined with other aggregate mutation operations.",
            badRequest.Value
        );
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitMutation_DeleteInstanceTerminalWorkflowCommit_AppliesAtomicTerminalMutation(
        bool deleteDataElement
    )
    {
        const int instanceVersion = 12;
        const int processStateVersion = 8;
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        DateTime processEnded = new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        DataElementInternal dataElement = new DataElement
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
        }.FromApiModel(null);
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(
            instanceGuid,
            deleteDataElement ? [dataElement] : [],
            new StorageVersions(instanceVersion, processStateVersion)
        );
        instanceInternal.Process = new ProcessState
        {
            Status = ProcessStatus.Processing,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1" },
        };
        string mutationJson = deleteDataElement
            ? $$"""
                {
                  "expectedProcessStatus": "processing",
                  "processState": {
                    "state": {
                      "status": "idle",
                      "ended": "{{processEnded:O}}",
                      "endEvent": "EndEvent_1"
                    },
                    "events": [
                      {
                        "eventType": "process_EndEvent",
                        "instanceId": "555/{{instanceGuid}}",
                        "user": {
                          "userId": 200001
                        }
                      }
                    ]
                  },
                  "deleteDataElements": [
                    {
                      "dataElementId": "{{dataElementId}}"
                    }
                  ],
                  "deleteInstance": {
                    "hard": true
                  }
                }
                """
            : $$"""
                {
                  "expectedProcessStatus": "processing",
                  "processState": {
                    "state": {
                      "status": "idle",
                      "ended": "{{processEnded:O}}",
                      "endEvent": "EndEvent_1"
                    },
                    "events": [
                      {
                        "eventType": "process_EndEvent",
                        "instanceId": "555/{{instanceGuid}}",
                        "user": {
                          "userId": 200001
                        }
                      }
                    ]
                  },
                  "deleteInstance": {
                    "hard": true
                  }
                }
                """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            mutationJson
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(InstanceEventType.Deleted, It.IsAny<InstanceInternal>())
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal instance) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        InstanceId = instance.Id.ToString(),
                        InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                    }
            );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal element
                ) => BuildDataElementEvent(eventType, instance, element)
            );
        InstanceMutationCommit capturedMutation = null;
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation,
            mutation =>
            {
                InstanceMutationApplyResult result = CreateApplyResult(
                    instanceInternal,
                    mutation,
                    new StorageVersions(instanceVersion + 1, processStateVersion + 1)
                );
                return result;
            }
        );

        ActionResult<InstanceMutationResponse> actionResult = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: instanceVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ifProcessStateVersionMatch: processStateVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            idempotencyKeyHeader: idempotencyKey.ToString()
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.False(response.Replayed);
        Assert.True(response.Instance.Status.IsHardDeleted);
        Assert.True(response.Instance.Status.IsSoftDeleted);
        Assert.Equal(ProcessStatus.Idle, response.Instance.Process.Status);
        Assert.Equal(processEnded, response.Instance.Process.Ended);
        Assert.Equal("EndEvent_1", response.Instance.Process.EndEvent);
        Assert.Null(response.Instance.Process.CurrentTask);
        Assert.Equal(
            (instanceVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            (processStateVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );

        Assert.NotNull(capturedMutation);
        Assert.Equal(instanceVersion, capturedMutation.ExpectedInstanceVersion);
        Assert.Equal(processStateVersion, capturedMutation.ExpectedProcessStateVersion);
        Assert.Equal(idempotencyKey, capturedMutation.IdempotencyKey);
        Assert.Equal(ProcessStatus.Idle, capturedMutation.InstanceChanges.Process.Status);
        Assert.Equal(processEnded, capturedMutation.InstanceChanges.Process.Ended);
        Assert.Equal("EndEvent_1", capturedMutation.InstanceChanges.Process.EndEvent);
        Assert.Null(capturedMutation.InstanceChanges.Process.CurrentTask);
        Assert.True(capturedMutation.InstanceChanges.Status.IsHardDeleted);
        Assert.Contains(
            capturedMutation.InstanceEvents,
            instanceEvent =>
                instanceEvent.EventType == InstanceEventType.process_EndEvent.ToString()
        );
        Assert.Contains(
            capturedMutation.InstanceEvents,
            instanceEvent =>
                instanceEvent.EventType == InstanceEventType.Deleted.ToString()
                && instanceEvent.DataId is null
        );
        Assert.Equal(deleteDataElement ? 1 : 0, capturedMutation.DeleteDataElements.Count);
        Assert.Equal(
            deleteDataElement ? 1 : 0,
            capturedMutation.InstanceEvents.Count(instanceEvent =>
                instanceEvent.EventType == InstanceEventType.Deleted.ToString()
                && instanceEvent.DataId == dataElementId.ToString()
            )
        );
        fixture.DataService.Verify(
            service =>
                service.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    dataElement,
                    7,
                    It.IsAny<CancellationToken>()
                ),
            deleteDataElement ? Times.Once() : Times.Never()
        );
    }

#pragma warning disable xUnit1026 // operationName provides a stable display label for each theory row.
    [Theory]
    [MemberData(nameof(DeleteInstanceCombinationRequests))]
    public async Task CommitMutation_DeleteInstanceHard_WhenCombinedWithOtherMutation_ReturnsBadRequest(
        string operationName,
        string mutationJson
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "deleteInstance cannot be combined with other aggregate mutation operations.",
            badRequest.Value
        );
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.IsAny<object>(),
                    AuthzConstants.POLICY_INSTANCE_DELETE
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
#pragma warning restore xUnit1026

    [Theory]
    [MemberData(nameof(InvalidTerminalDeleteInstanceRequests))]
    public async Task CommitMutation_DeleteInstanceTerminalWorkflowCommit_WhenShapeIsInvalid_ReturnsBadRequest(
        string mutationJson
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            mutationJson
        );
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "12",
            ifProcessStateVersionMatch: "8",
            idempotencyKeyHeader: Guid.NewGuid().ToString()
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "deleteInstance cannot be combined with other aggregate mutation operations.",
            badRequest.Value
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Theory]
    [InlineData(StorageHeaders.IfInstanceVersionMatch)]
    [InlineData(StorageHeaders.IfProcessStateVersionMatch)]
    [InlineData(StorageHeaders.IdempotencyKey)]
    public async Task CommitMutation_DeleteInstanceTerminalWorkflowCommit_WhenWorkflowHeaderIsMissing_ReturnsBadRequest(
        string missingHeader
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "expectedProcessStatus": "processing",
              "processState": {
                "state": {
                  "status": "idle",
                  "ended": "2026-06-07T08:09:10Z",
                  "endEvent": "EndEvent_1"
                }
              },
              "deleteInstance": {
                "hard": true
                }
            }
            """
        );
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: missingHeader == StorageHeaders.IfInstanceVersionMatch
                ? null
                : "12",
            ifProcessStateVersionMatch: missingHeader == StorageHeaders.IfProcessStateVersionMatch
                ? null
                : "8",
            idempotencyKeyHeader: missingHeader == StorageHeaders.IdempotencyKey
                ? null
                : Guid.NewGuid().ToString()
        );

        Assert.IsType<BadRequestObjectResult>(result.Result);
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    public static TheoryData<string, string> DeleteInstanceCombinationRequests()
    {
        return new()
        {
            {
                "dataValues",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "dataValues": {
                        "status": "deleted"
                      }
                    }
                    """
            },
            {
                "presentationTexts",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "presentationTexts": {
                        "status": "deleted"
                      }
                    }
                    """
            },
            {
                "addCompleteConfirmation",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "addCompleteConfirmation": true
                    }
                    """
            },
            {
                "processState.state",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "processState": {
                        "state": {
                          "started": "2024-01-01T00:00:00Z"
                        }
                      }
                    }
                    """
            },
            {
                "processState.events",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "processState": {
                        "events": [
                          {
                            "eventType": "process_StartEvent",
                            "instanceId": "555/11111111-1111-1111-1111-111111111111"
                          }
                        ]
                      }
                    }
                    """
            },
            {
                "createDataElements",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "createDataElements": [
                        {
                          "dataType": "attachment",
                          "contentPartName": "attachment"
                        }
                      ]
                    }
                    """
            },
            {
                "updateDataElements",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "updateDataElements": [
                        {
                          "dataElementId": "11111111-1111-1111-1111-111111111111",
                          "locked": true
                        }
                      ]
                    }
                    """
            },
            {
                "deleteDataElements",
                """
                    {
                      "deleteInstance": {
                        "hard": true
                      },
                      "deleteDataElements": [
                        {
                          "dataElementId": "11111111-1111-1111-1111-111111111111"
                        }
                      ]
                    }
                    """
            },
        };
    }

    public static TheoryData<string> InvalidTerminalDeleteInstanceRequests()
    {
        const string endedState = """
            "processState": {
              "state": {
                "status": "idle",
                "ended": "2026-06-07T08:09:10Z",
                "endEvent": "EndEvent_1"
              }
            }
            """;
        return new()
        {
            {
                $$"""
                    {
                      {{endedState}},
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "idle",
                      {{endedState}},
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "processing",
                      "processState": {
                        "state": {
                          "ended": "2026-06-07T08:09:10Z",
                          "endEvent": "EndEvent_1"
                        }
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "processing",
                      "processState": {
                        "state": {
                          "status": "processing",
                          "ended": "2026-06-07T08:09:10Z",
                          "endEvent": "EndEvent_1"
                        }
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                """
                    {
                      "expectedProcessStatus": "processing",
                      "processState": {
                        "state": {
                          "status": "idle",
                          "endEvent": "EndEvent_1"
                        }
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                """
                    {
                      "expectedProcessStatus": "processing",
                      "processState": {
                        "state": {
                          "status": "idle",
                          "ended": "2026-06-07T08:09:10Z",
                          "endEvent": "EndEvent_1",
                          "currentTask": {
                            "elementId": "Task_1"
                          }
                        }
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                """
                    {
                      "expectedProcessStatus": "processing",
                      "processState": {
                        "state": {
                          "status": "idle",
                          "ended": "2026-06-07T08:09:10Z"
                        }
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "processing",
                      {{endedState}},
                      "dataValues": {
                        "unrelated": "update"
                      },
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "processing",
                      {{endedState}},
                      "createDataElements": [
                        {
                          "dataType": "attachment",
                          "contentPartName": "attachment"
                        }
                      ],
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
            {
                $$"""
                    {
                      "expectedProcessStatus": "processing",
                      {{endedState}},
                      "updateDataElements": [
                        {
                          "dataElementId": "11111111-1111-1111-1111-111111111111",
                          "locked": true
                        }
                      ],
                      "deleteInstance": { "hard": true }
                    }
                    """
            },
        };
    }

    [Fact]
    public async Task CommitMutation_DeleteInstanceTerminalWorkflowCommit_WhenIdempotencyReplaysAfterHardDelete_ReturnsOriginalResult()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        DateTime deletedAt = DateTime.UtcNow;
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(
            instanceGuid,
            [],
            new StorageVersions(13, 9)
        );
        instanceInternal.Status = new InstanceStatus
        {
            IsHardDeleted = true,
            IsSoftDeleted = true,
            HardDeleted = deletedAt,
            SoftDeleted = deletedAt,
        };
        instanceInternal.Process = new ProcessState
        {
            Status = ProcessStatus.Idle,
            Ended = deletedAt,
            EndEvent = "EndEvent_1",
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            """
            {
              "expectedProcessStatus": "processing",
              "processState": {
                "state": {
                  "status": "idle",
                  "ended": "2026-06-07T08:09:10Z",
                  "endEvent": "EndEvent_1"
                }
              },
              "deleteInstance": {
                "hard": true
              }
            }
            """
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, [], instanceInternal));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            idempotencyKeyHeader: idempotencyKey.ToString(),
            ifInstanceVersionMatch: "12",
            ifProcessStateVersionMatch: "8"
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.True(response.Replayed);
        Assert.True(response.Instance.Status.IsHardDeleted);
        Assert.Equal(ProcessStatus.Idle, response.Instance.Process.Status);
        Assert.Equal("EndEvent_1", response.Instance.Process.EndEvent);
        fixture.MutationRepository.Verify(
            repository =>
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object>(),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                ),
            Times.Never
        );
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
    public async Task CommitMutation_AddCompleteConfirmation_CarriesOnlyTheCallingStakeholdersConfirmation()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        instanceInternal.CompleteConfirmations =
        [
            new CompleteConfirmation
            {
                StakeholderId = "other-org",
                ConfirmedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        ];
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            """
            {
              "addCompleteConfirmation": true
            }
            """
        );
        fixture.HttpContext.User = CreateOrgPrincipal(_org);
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.ConfirmedComplete,
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal instance) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        InstanceId = instance.Id.ToString(),
                        InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                    }
            );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedMutation);
        Assert.NotNull(capturedMutation.InstanceChanges.CompleteConfirmations);
        CompleteConfirmation addedConfirmation = Assert.Single(
            capturedMutation.InstanceChanges.CompleteConfirmations
        );
        Assert.Equal(_org, addedConfirmation.StakeholderId);
        Assert.Equal(capturedMutation.Stamp.LastChanged, addedConfirmation.ConfirmedOn);
        Assert.Single(
            capturedMutation.InstanceEvents,
            instanceEvent =>
                instanceEvent.EventType == InstanceEventType.ConfirmedComplete.ToString()
        );
    }

    [Fact]
    public async Task CommitMutation_AddCompleteConfirmation_WhenStakeholderHasConfirmed_LeavesTheDecisionToTheMerge()
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        instanceInternal.CompleteConfirmations =
        [
            new CompleteConfirmation
            {
                StakeholderId = _org,
                ConfirmedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        ];
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
            CreateAggregateApplication(),
            """
            {
              "addCompleteConfirmation": true,
              "dataValues": {
                "eFormidlingShipmentStatus": "levert"
              }
            }
            """
        );
        fixture.HttpContext.User = CreateOrgPrincipal(_org);
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedMutation);
        Assert.NotNull(capturedMutation.InstanceChanges.DataValues);

        // The snapshot is read before the instance row is locked, so the controller does not filter
        // on it. mergeinstanceupdate drops a confirmation the stakeholder already has, which is what
        // settles two callers confirming at once.
        Assert.NotNull(capturedMutation.InstanceChanges.CompleteConfirmations);
        CompleteConfirmation sentConfirmation = Assert.Single(
            capturedMutation.InstanceChanges.CompleteConfirmations
        );
        Assert.Equal(_org, sentConfirmation.StakeholderId);
    }

    [Fact]
    public async Task CommitMutation_AddCompleteConfirmation_WhenCompletePolicyFails_ReturnsForbid()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "addCompleteConfirmation": true
            }
            """
        );
        fixture.HttpContext.User = CreateOrgPrincipal(_org);
        fixture
            .PolicyAuthorizationService.Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_COMPLETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Failed());

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_AddCompleteConfirmation_WithoutOrganisation_ReturnsForbidBeforePolicy()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, []),
            CreateAggregateApplication(),
            """
            {
              "addCompleteConfirmation": true
            }
            """
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result.Result);
        fixture.PolicyAuthorizationService.Verify(
            service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.IsAny<object>(),
                    AuthzConstants.POLICY_INSTANCE_COMPLETE
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        InstanceEvent createdEvent = Assert.Single(capturedMutation.InstanceEvents);
        DataElementCreation create = Assert.Single(capturedMutation.CreateDataElements);
        DataElementInternal createdDataElement = create.Element;
        Assert.Equal(BlobVersionId.Decode(createdDataElement.BlobVersionId), create.BlobVersion);
        Assert.Equal(InstanceEventType.Created.ToString(), createdEvent.EventType);
        Assert.Equal(createdDataElement.Id.ToString(), createdEvent.DataId);
        Assert.NotEqual(Guid.Empty, createdDataElement.Id);
        Assert.Empty(createdDataElement.References);
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementInternal createdDataElement = Assert
            .Single(capturedMutation.CreateDataElements)
            .Element;
        Assert.NotEqual(callerSuppliedDataElementId, createdDataElement.Id);
        Assert.NotEqual(Guid.Empty, createdDataElement.Id);
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
        InstanceInternal instanceInternal = CreateAggregateInstanceInternal(instanceGuid, []);
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instanceInternal,
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation =>
            {
                capturedMutation = mutation;
                updatedInstanceInternal = CreateAggregateInstanceInternal(
                    instanceGuid,
                    [.. mutation.CreateDataElements.Select(create => create.Element)],
                    new StorageVersions(2, 1)
                );
            },
            mutation =>
                CreateApplyResult(fixture.InstanceInternal, mutation, new StorageVersions(2, 1))
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        List<string> createdIds =
        [
            .. capturedMutation.CreateDataElements.Select(create => create.Element.Id.ToString()),
        ];
        Assert.Equal(createdIds, response.CreatedDataElementIds);
        Assert.False(response.Replayed);
        Assert.DoesNotContain(callerSuppliedFirstId.ToString(), createdIds);
        Assert.DoesNotContain(callerSuppliedSecondId.ToString(), createdIds);
        Assert.Equal(2, createdIds.Select(Guid.Parse).Distinct().Count());
        Assert.All(
            capturedMutation.CreateDataElements,
            create => Assert.Equal(instanceGuid, create.Element.InstanceGuid)
        );
        fixture.MutationRepository.Verify(
            repository =>
                repository.Apply(
                    instanceGuid,
                    fixture.InstanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        Assert.All(
            createdIds,
            createdId =>
                Assert.Contains(response.Instance.Data, dataElement => dataElement.Id == createdId)
        );
        Assert.All(
            capturedMutation.CreateDataElements,
            createdDataElement =>
                Assert.Equal(
                    createdDataElement.Element.BlobVersionId,
                    response
                        .Instance.Data.Single(dataElement =>
                            dataElement.Id == createdDataElement.Element.Id.ToString()
                        )
                        .BlobVersionId
                )
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
        DateTime originalLastChanged = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        InstanceMutationCommit capturedMutation = null;
        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            BlobStoragePath = "legacy/path",
            ContentType = "application/old",
            Filename = "old.bin",
            Size = 3,
            LastChanged = originalLastChanged,
            LastChangedBy = "previous-user",
            Refs = [Guid.NewGuid()],
            References =
            [
                new Reference
                {
                    Value = "Task_Previous",
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                },
            ],
        };
        Application application = CreateAggregateApplication();
        application.DataTypes[0].EnableFileScan = true;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [dataElement.FromApiModel(null)]),
            application,
            $$"""
            {
              "dataValues": {},
              "presentationTexts": {},
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
                    It.Is<DataElementInternal>(element => element.Id == dataElementId)
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal eventDataElement
                ) => BuildDataElementEvent(eventType, instance, eventDataElement)
            );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
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
        DataElementUpdate updatedDataElement = Assert.Single(capturedMutation.UpdateDataElements);
        Assert.Null(capturedMutation.InstanceChanges);
        Assert.NotNull(updatedDataElement.NewBlobVersion);
        Assert.NotEqual(Guid.Empty, updatedDataElement.NewBlobVersion.Value);
        Assert.True(updatedDataElement.Changes.Refs.IsSpecified);
        Assert.Null(updatedDataElement.Changes.Refs.Value);
        Assert.True(updatedDataElement.Changes.References.IsSpecified);
        Assert.Empty(updatedDataElement.Changes.References.Value);
        DataElementInternal originalElement = Assert.Single(fixture.InstanceInternal.Data);
        Assert.Equal(originalLastChanged, originalElement.LastChanged);
        Assert.Equal("previous-user", originalElement.LastChangedBy);
        Assert.Equal(dataElement.Refs, originalElement.Refs);
        Assert.Equal("Task_Previous", Assert.Single(originalElement.References).Value);
        fixture.DataService.Verify(
            service =>
                service.StartFileScan(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataType>(),
                    It.Is<DataElementInternal>(element =>
                        element.Id == dataElementId
                        && element.ContentType == "text/plain"
                        && element.Filename == "attachment.txt"
                        && element.Size == 12
                        && element.FileScanResult == FileScanResult.Pending
                        && element.LastChanged == originalLastChanged
                        && element.LastChangedBy == "previous-user"
                        && element.BlobStoragePath != "legacy/path"
                    ),
                    It.IsAny<DateTimeOffset>(),
                    7,
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
    }

    [Theory]
    [InlineData("EREREREREREREREREREREQ", "EREREREREREREREREREREQ", null)]
    [InlineData(
        "\"EREREREREREREREREREREQ\"",
        null,
        "expectedCurrentBlobVersion must identify a blob version id."
    )]
    [InlineData(
        "W/\"EREREREREREREREREREREQ\"",
        null,
        "expectedCurrentBlobVersion must identify a blob version id."
    )]
    [InlineData(
        "not-a-blob-version",
        null,
        "expectedCurrentBlobVersion must identify a blob version id."
    )]
    public async Task CommitMutation_UpdateDataElement_NormalizesExpectedCurrentBlobVersion(
        string expectedCurrentBlobVersion,
        string expectedNormalizedBlobVersion,
        string expectedError
    )
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
                  "tags": ["metadata-change"],
                  "expectedCurrentBlobVersion": {{JsonSerializer.Serialize(
                expectedCurrentBlobVersion
            )}}
                }
              ]
            }
            """
        );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        if (expectedError is null)
        {
            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(
                expectedNormalizedBlobVersion is null
                    ? (Guid?)null
                    : BlobVersionId.Decode(expectedNormalizedBlobVersion),
                Assert.Single(capturedMutation.UpdateDataElements).ExpectedCurrentBlobVersion
            );
            fixture.MutationRepository.Verify(
                repository =>
                    repository.Apply(
                        instanceGuid,
                        fixture.InstanceInternal.InternalId,
                        It.IsAny<InstanceMutationCommit>(),
                        It.IsAny<CancellationToken>()
                    ),
                Times.Once
            );
            return;
        }

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(expectedError, badRequest.Value);
        Assert.Null(capturedMutation);
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
    public async Task CommitMutation_IdempotentReplay_ReturnsBeforeCurrentAuthorization()
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
              ],
              "dataValues": {
                "status": "retry"
              }
            }
            """
        );
        fixture
            .ProcessAuthorizer.Setup(authorizer =>
                authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>())
            )
            .ReturnsAsync(false);
        fixture
            .MutationRepository.Setup(repository =>
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, [], fixture.InstanceInternal));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "12",
            ifProcessStateVersionMatch: "8",
            idempotencyKeyHeader: idempotencyKey.ToString()
        );

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        InstanceMutationResponse response = Assert.IsType<InstanceMutationResponse>(ok.Value);
        Assert.True(response.Replayed);
        fixture.MutationRepository.Verify(
            repository =>
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.ProcessAuthorizer.Verify(
            authorizer => authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>()),
            Times.Never
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CommitMutation_MismatchingClientVersionFastFailsBeforeAuthorizationAndWork(
        bool instanceVersionMismatch,
        bool processStateVersionMismatch
    )
    {
        const int instanceVersion = 7;
        const int processStateVersion = 3;
        Guid instanceGuid = Guid.NewGuid();
        InstanceInternal snapshot = CreateAggregateInstanceInternal(
            instanceGuid,
            [],
            new StorageVersions(instanceVersion, processStateVersion)
        );
        snapshot.Process = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1", AltinnTaskType = "data" },
        };
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            snapshot,
            CreateAggregateApplication(),
            """
            {
              "createDataElements": [
                {
                  "dataType": "attachment",
                  "contentPartName": "attachment"
                }
              ],
              "dataValues": {
                "summary": "ready"
              },
              "presentationTexts": {
                "summary": "ready"
              },
              "processState": {
                "state": {
                  "currentTask": {
                    "elementId": "Task_2",
                    "altinnTaskType": "data"
                  }
                }
              }
            }
            """,
            CreateFormFile("attachment")
        );
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: (
                instanceVersionMismatch ? instanceVersion - 1 : instanceVersion
            ).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ifProcessStateVersionMatch: (
                processStateVersionMismatch ? processStateVersion - 1 : processStateVersion
            ).ToString(System.Globalization.CultureInfo.InvariantCulture)
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(preconditionFailed.Value);
        Assert.Equal(
            instanceVersionMismatch
                ? "instance_version_mismatch"
                : "process_state_version_mismatch",
            problem.Type
        );
        Assert.Equal(
            instanceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            processStateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
        fixture.ProcessAuthorizer.Verify(
            authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                ),
            Times.Never
        );
        fixture.ProcessAuthorizer.Verify(
            authorizer => authorizer.AuthorizePresentationTextsUpdate(It.IsAny<InstanceInternal>()),
            Times.Never
        );
        fixture.ProcessAuthorizer.Verify(
            authorizer => authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>()),
            Times.Never
        );
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
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_WriteTimeProcessStateVersionMismatchReturnsExistingProblemDetails()
    {
        Guid instanceGuid = Guid.NewGuid();
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
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    fixture.InstanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStateVersionMismatchException(9, 5));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        ObjectResult preconditionFailed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, preconditionFailed.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(preconditionFailed.Value);
        Assert.Equal("process_state_version_mismatch", problem.Type);
        Assert.Equal(
            "9",
            fixture.HttpContext.Response.Headers[StorageHeaders.InstanceVersion].Single()
        );
        Assert.Equal(
            "5",
            fixture.HttpContext.Response.Headers[StorageHeaders.ProcessStateVersion].Single()
        );
    }

    [Fact]
    public async Task CommitMutation_RepositoryProcessStatusConflict_ReturnsConflictWithCurrentStatus()
    {
        Guid instanceGuid = Guid.NewGuid();
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            CreateAggregateInstanceInternal(instanceGuid, [], new StorageVersions(7, 3)),
            CreateAggregateApplication(),
            """{"dataValues":{"value":"updated"}}"""
        );
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    fixture.InstanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing));

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        JsonResult conflict = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.ContentType);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("process_status_conflict", problem.Type);
        Assert.Equal("Process status conflict", problem.Title);
        Assert.Contains("processing", problem.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProcessStatus.Processing, null)]
    [InlineData(ProcessStatus.Idle, ProcessStatus.Processing)]
    public async Task CommitMutation_ExpectedProcessStatusMismatch_ReturnsConflictBeforeApply(
        ProcessStatus currentProcessStatus,
        ProcessStatus? expectedProcessStatus
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        InstanceInternal instance = CreateAggregateInstanceInternal(
            instanceGuid,
            [],
            new StorageVersions(7, 3)
        );
        instance.Process = new ProcessState { Status = currentProcessStatus };
        string mutationJson = expectedProcessStatus is null
            ? """{"dataValues":{"value":"updated"}}"""
            : $$"""
                {
                  "expectedProcessStatus": {{JsonSerializer.Serialize(expectedProcessStatus)}},
                  "dataValues": {
                    "value": "updated"
                  }
                }
                """;
        AggregateMutationFixture fixture = CreateAggregateMutationFixture(
            instanceGuid,
            instance,
            CreateAggregateApplication(),
            mutationJson
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        JsonResult conflict = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains(
            currentProcessStatus.ToString().ToLowerInvariant(),
            problem.Detail,
            StringComparison.Ordinal
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
                repository.GetReplayResult(
                    instanceGuid,
                    12,
                    13,
                    9,
                    idempotencyKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(true, [], fixture.InstanceInternal));
        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None,
            ifInstanceVersionMatch: "12",
            idempotencyKeyHeader: idempotencyKey.ToString()
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

    [Theory]
    [InlineData("")]
    [InlineData(""", "contentPartName":" ", "tags":null, "locked":null""")]
    public async Task CommitMutation_UpdateDataElementWithoutChanges_ReturnsBadRequest(
        string suppliedFields
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
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
                  "dataElementId": "{{dataElementId}}"{{suppliedFields}}
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

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            $"No metadata or content changes were supplied for data element {dataElementId}.",
            badRequest.Value
        );
        InstanceMutationAsserts.VerifyApplyNever(fixture.MutationRepository);
    }

    [Fact]
    public async Task CommitMutation_UpdateDataElementEmptyGeneratedFromTask_ClearsReferences()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        InstanceMutationCommit capturedMutation = null;
        DataElement existingDataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = _dataType,
            References =
            [
                new Reference
                {
                    Value = "Task_Previous",
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                },
            ],
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
                  "generatedFromTask": ""
                }
              ]
            }
            """
        );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementUpdate capturedUpdate = Assert.Single(capturedMutation.UpdateDataElements);
        Assert.True(capturedUpdate.Changes.References.IsSpecified);
        Assert.Empty(capturedUpdate.Changes.References.Value);
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementUpdate capturedUpdate = Assert.Single(capturedMutation.UpdateDataElements);
        Assert.True(capturedUpdate.IgnoreLock);
        Assert.Equal(false, capturedUpdate.Changes.Locked);
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
                  "tags": ["metadata-change"],
                  "refs": null,
                  "generatedFromTask": null,
                  "metadata": null,
                  "userDefinedMetadata": null
                }
              ]
            }
            """
        );
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementUpdate capturedUpdate = Assert.Single(capturedMutation.UpdateDataElements);
        Assert.False(capturedUpdate.IgnoreLock);
        Assert.True(capturedUpdate.Changes.Tags.IsSpecified);
        Assert.Equal(["metadata-change"], capturedUpdate.Changes.Tags.Value);
        Assert.False(capturedUpdate.Changes.Refs.IsSpecified);
        Assert.False(capturedUpdate.Changes.References.IsSpecified);
        Assert.False(capturedUpdate.Changes.Metadata.IsSpecified);
        Assert.False(capturedUpdate.Changes.UserDefinedMetadata.IsSpecified);
        Assert.Null(capturedUpdate.Changes.Locked);
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        DataElementUpdate capturedUpdate = Assert.Single(capturedMutation.UpdateDataElements);
        Assert.False(capturedUpdate.IgnoreLock);
        Assert.Equal(false, capturedUpdate.Changes.Locked);
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation,
            mutation =>
            {
                DataElementUpdate update = Assert.Single(mutation.UpdateDataElements);
                DataElement stampedDataElement = new()
                {
                    Id = dataElementId.ToString(),
                    InstanceGuid = instanceGuid.ToString(),
                    DataType = _dataType,
                    Metadata = [.. update.Changes.Metadata.Value],
                    LastChanged = mutation.Stamp.LastChanged,
                    LastChangedBy = mutation.Stamp.LastChangedBy,
                };
                InstanceInternal stampedInstance = InstanceMutationTestFactory.ApplyInstanceChanges(
                    fixture.InstanceInternal,
                    mutation
                );
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
        DataElementUpdate updatedDataElement = Assert.Single(capturedMutation.UpdateDataElements);
        List<KeyValueEntry> metadata = Assert.IsType<List<KeyValueEntry>>(
            updatedDataElement.Changes.Metadata.Value
        );
        KeyValueEntry entry = Assert.Single(metadata);
        Assert.Equal("changed", entry.Key);
        Assert.Equal("metadata", entry.Value);
        Assert.Equal(DateTimeKind.Utc, capturedMutation.Stamp.LastChanged.Kind);
        Assert.Equal("200001", capturedMutation.Stamp.LastChangedBy);
        Assert.Empty(capturedMutation.InstanceEvents);
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
        DataElement responseDataElement = Assert.Single(response.Instance.Data);
        Assert.Equal(capturedMutation.Stamp.LastChanged, responseDataElement.LastChanged);
        Assert.Equal(capturedMutation.Stamp.LastChangedBy, responseDataElement.LastChangedBy);
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        string createdDataElementId = Assert
            .Single(capturedMutation.CreateDataElements)
            .Element.Id.ToString();
        InstanceEvent processEvent = capturedMutation.InstanceEvents[0];
        Assert.NotNull(processEvent.Id);
        Assert.NotEqual(Guid.Empty, processEvent.Id.Value);
        Assert.Equal($"555/{instanceGuid}", processEvent.InstanceId);
        Assert.NotNull(processEvent.Created);
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
        ProcessState snapshotProcess = instanceInternal.Process;
        InstanceMutationCommit capturedMutation = null;
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
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal instance,
                    DataElementInternal dataElement
                ) => BuildDataElementEvent(eventType, instance, dataElement)
            );

        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(snapshotProcess, instanceInternal.Process);
        Assert.Equal("Task_Old", snapshotProcess.CurrentTask.ElementId);
        Assert.Collection(
            capturedMutation.InstanceEvents,
            created =>
            {
                Assert.Equal(InstanceEventType.Created.ToString(), created.EventType);
                Assert.NotEqual(Guid.Empty, Guid.Parse(created.DataId));
                Assert.Equal("Task_Updated", created.ProcessInfo.CurrentTask.ElementId);
                Assert.Equal("data", created.ProcessInfo.CurrentTask.AltinnTaskType);
            },
            saved =>
            {
                Assert.Equal(InstanceEventType.Saved.ToString(), saved.EventType);
                Assert.Equal(updateDataElementId.ToString(), saved.DataId);
                Assert.Equal("Task_Updated", saved.ProcessInfo.CurrentTask.ElementId);
                Assert.Equal("data", saved.ProcessInfo.CurrentTask.AltinnTaskType);
            }
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitMutation_WhenAggregateApplyFailsAfterStaging_CleansUpAndRunsNoPostCommitWork(
        bool processStatusConflict
    )
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
            .ThrowsAsync(
                processStatusConflict
                    ? new ProcessStatusConflictException(ProcessStatus.Processing)
                    : new RepositoryException("event insert failed", HttpStatusCode.Conflict)
            );

        ActionResult<InstanceMutationResponse> result = await fixture.Sut.CommitMutation(
            555,
            instanceGuid,
            CancellationToken.None
        );

        if (processStatusConflict)
        {
            JsonResult conflict = Assert.IsType<JsonResult>(result.Result);
            ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal("process_status_conflict", problem.Type);
        }
        else
        {
            ObjectResult genericConflict = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status409Conflict, genericConflict.StatusCode);
            Assert.Equal("event insert failed", Assert.IsType<string>(genericConflict.Value));
        }
        fixture.BlobRepository.Verify(
            repository =>
                repository.DeleteBlob(
                    _org,
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
                    DataElementHelper.GetVersionedBlobPath(
                        _appId,
                        instanceGuid,
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
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
        );
        fixture
            .InstanceEventService.Setup(service =>
                service.BuildInstanceEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<DataElementInternal>(element => element.Id == dataElementId)
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
                    It.Is<DataElementInternal>(element => element.Id == dataElementId)
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
        DataElementDeletion capturedDelete = Assert.Single(capturedMutation.DeleteDataElements);
        Assert.Equal(dataElementId, capturedDelete.DataElementId);
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
                    It.Is<DataElementInternal>(element => element.Id == dataElementId)
                )
            )
            .Returns(new InstanceEvent { EventType = InstanceEventType.Deleted.ToString() });
        SetupCapturingMutationRepository(
            fixture,
            instanceGuid,
            mutation => capturedMutation = mutation
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
        DataElementDeletion capturedDelete = Assert.Single(capturedMutation.DeleteDataElements);
        Assert.Equal(dataElementId, capturedDelete.DataElementId);
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
        Mock<IAuthorizationService> policyAuthorizationServiceMock = new();
        Mock<IProcessAuthorizer> processAuthorizerMock = new();

        instanceEventServiceMock
            .Setup(service =>
                service.BuildInstanceEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>()
                )
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal instance) =>
                    new InstanceEvent
                    {
                        Id = Guid.NewGuid(),
                        EventType = eventType.ToString(),
                        InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
                        InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                        ProcessInfo = instance.Process,
                        Created = DateTime.UtcNow,
                    }
            );
        instanceEventServiceMock
            .Setup(service =>
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
        policyAuthorizationServiceMock
            .Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_DELETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Success());
        policyAuthorizationServiceMock
            .Setup(service =>
                service.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.Is<object>(resource => resource == null),
                    AuthzConstants.POLICY_INSTANCE_COMPLETE
                )
            )
            .ReturnsAsync(AuthorizationResult.Success());

        DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(200001, 1337) };
        if (mutationJson is not null)
        {
            SetMultipartMutationRequest(httpContext, mutationJson, fileParts);
        }

        InstanceMutationsController sut = new(
            dataRepositoryMock.Object,
            blobRepositoryMock.Object,
            instanceRepositoryMock.Object,
            mutationRepositoryMock.Object,
            applicationRepositoryMock.Object,
            dataServiceMock.Object,
            instanceEventServiceMock.Object,
            Options.Create(new GeneralSettings { Hostname = "https://altinn.no/" }),
            authorizationServiceMock.Object,
            policyAuthorizationServiceMock.Object,
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
            policyAuthorizationServiceMock,
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
            Id = Guid.NewGuid(),
            EventType = eventType.ToString(),
            InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
            DataId = dataElement.Id.ToString(),
            InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
            ProcessInfo = instance.Process,
        };

    private static ClaimsPrincipal CreateOrgPrincipal(string org) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(AltinnCoreClaimTypes.Org, org),
                    new Claim(AltinnCoreClaimTypes.OrgNumber, "111111111"),
                ],
                "test"
            )
        );

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
            Data = [.. dataElements.Select(dataElement => dataElement.ToApiModel())],
        };

        return InstanceInternalTestFactory.Create(
            instance,
            dataElements,
            InternalId: 123L,
            versions: versions
        );
    }

    private static void SetupCapturingMutationRepository(
        AggregateMutationFixture fixture,
        Guid instanceGuid,
        Action<InstanceMutationCommit> captureMutation,
        Func<InstanceMutationCommit, InstanceMutationApplyResult> createApplyResult = null
    )
    {
        createApplyResult ??= mutation => CreateApplyResult(fixture.InstanceInternal, mutation);
        fixture
            .MutationRepository.Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    fixture.InstanceInternal.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    captureMutation(mutation);
                    return createApplyResult(mutation);
                }
            );
    }

    private static InstanceMutationApplyResult CreateApplyResult(
        InstanceInternal instance,
        InstanceMutationCommit mutation,
        StorageVersions versions = null
    )
    {
        List<DataElementInternal> dataElements = [.. instance.Data];
        foreach (DataElementDeletion deleteDataElement in mutation.DeleteDataElements ?? [])
        {
            dataElements.RemoveAll(dataElement =>
                dataElement.Id == deleteDataElement.DataElementId
            );
        }

        dataElements.AddRange(mutation.CreateDataElements.Select(create => create.Element));

        instance = InstanceMutationTestFactory.ApplyInstanceChanges(instance, mutation);

        instance.Data = dataElements;
        instance.Versions = versions ?? instance.Versions;

        return new InstanceMutationApplyResult(
            false,
            [.. mutation.CreateDataElements.Select(create => create.Element.Id.ToString())],
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
        InstanceMutationsController Sut,
        DefaultHttpContext HttpContext,
        InstanceInternal InstanceInternal,
        Mock<IDataRepository> DataRepository,
        Mock<IBlobRepository> BlobRepository,
        Mock<IInstanceRepository> InstanceRepository,
        Mock<IDataService> DataService,
        Mock<IInstanceMutationRepository> MutationRepository,
        Mock<IInstanceEventService> InstanceEventService,
        Mock<IAuthorizationService> PolicyAuthorizationService,
        Mock<IProcessAuthorizer> ProcessAuthorizer
    );
}
