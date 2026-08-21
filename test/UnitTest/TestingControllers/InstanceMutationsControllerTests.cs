#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Represents a collection of integration tests of the <see cref="InstanceMutationsController"/>.
/// </summary>
public class InstanceMutationsControllerTests(
    TestApplicationFactory<InstanceMutationsController> factory
) : IClassFixture<TestApplicationFactory<InstanceMutationsController>>
{
    private const string _versionPrefix = "/storage/api/v1";
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData("duplicate-updates")]
    [InlineData("duplicate-deletes")]
    [InlineData("update-and-delete")]
    public async Task CommitMutation_DuplicateDataElementMutationIds_ReturnsBadRequest(
        string requestShape
    )
    {
        // Arrange
        Guid dataElementId = Guid.Parse(SensitiveDataApp.DataElements.Default);
        InstanceMutationRequest request = requestShape switch
        {
            "duplicate-updates" => new InstanceMutationRequest
            {
                UpdateDataElements =
                [
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = false,
                    },
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = true,
                    },
                ],
            },
            "duplicate-deletes" => new InstanceMutationRequest
            {
                DeleteDataElements =
                [
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                ],
            },
            "update-and-delete" => new InstanceMutationRequest
            {
                UpdateDataElements =
                [
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = true,
                    },
                ],
                DeleteDataElements =
                [
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(requestShape)),
        };

        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock
        );

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );
        string content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ExpectedDuplicateDataElementMutationIdsResponse(dataElementId), content);
        InstanceMutationAsserts.VerifyApplyNever(mutationRepositoryMock);
    }

    [Fact]
    public async Task CommitMutation_MultipartRequest_EnvelopeSurvivesModelBinding()
    {
        // Arrange
        Guid dataElementId = Guid.Parse(SensitiveDataApp.DataElements.Default);
        InstanceMutationRequest request = new()
        {
            UpdateDataElements =
            [
                new InstanceMutationUpdateDataElement
                {
                    DataElementId = dataElementId,
                    Locked = false,
                },
                new InstanceMutationUpdateDataElement
                {
                    DataElementId = dataElementId,
                    Locked = true,
                },
            ],
        };

        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock
        );

        using MultipartFormDataContent multipartContent = new();
        multipartContent.Add(JsonContent.Create(request, options: _serializerOptions), "mutation");

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            multipartContent
        );
        string content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ExpectedDuplicateDataElementMutationIdsResponse(dataElementId), content);
        InstanceMutationAsserts.VerifyApplyNever(mutationRepositoryMock);
    }

    [Fact]
    public async Task CommitMutation_RepositoryConflicts_OnlyProcessStatusUsesProblemDetailsResponse()
    {
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            status: null
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .SetupSequence(repository =>
                repository.Apply(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    storedInstance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing))
            .ThrowsAsync(new RepositoryException("unrelated conflict", HttpStatusCode.Conflict));
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            DataValues = new Dictionary<string, string> { ["value"] = "updated" },
        };

        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument responseBody = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync()
        );
        Assert.Equal(
            "process_status_conflict",
            responseBody.RootElement.GetProperty("type").GetString()
        );
        Assert.Equal(
            (int)HttpStatusCode.Conflict,
            responseBody.RootElement.GetProperty("status").GetInt32()
        );
        Assert.Contains(
            "processing",
            responseBody.RootElement.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );

        HttpResponseMessage unrelatedResponse = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        Assert.Equal(HttpStatusCode.Conflict, unrelatedResponse.StatusCode);
        Assert.Equal("application/json", unrelatedResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"unrelated conflict\"", await unrelatedResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitMutation_NewlyEndedProcess_ArchivesResponseAndStoredInstance(
        bool withExistingStatus
    )
    {
        // Arrange
        DateTime ended = new(2026, 7, 10, 9, 8, 7, DateTimeKind.Utc);
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            withExistingStatus ? new InstanceStatus { ReadStatus = ReadStatus.Read } : null
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    Ended = ended,
                    EndEvent = "EndEvent_1",
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string responseBody = await response.Content.ReadAsStringAsync();
        if (!withExistingStatus)
        {
            Assert.Equal(
                """{"instance":{"id":"1337/99194777-a691-433a-ace1-225e9a691653","instanceOwner":{"partyId":"1337"},"appId":"ttd/sensitive-data","org":"ttd","selfLinks":{"platform":"https://platform.at22.altinn.cloud/storage/api/v1/instances/1337/99194777-a691-433a-ace1-225e9a691653"},"process":{"started":"2026-07-10T08:00:00Z","startEvent":"StartEvent_1","ended":"2026-07-10T09:08:07Z","endEvent":"EndEvent_1"},"status":{"isArchived":true,"archived":"2026-07-10T09:08:07Z","isSoftDeleted":false,"isHardDeleted":false,"readStatus":"Unread"},"data":[]},"createdDataElementIds":[],"replayed":false}""",
                responseBody
            );
        }

        using JsonDocument responseJson = JsonDocument.Parse(responseBody);
        JsonElement responseStatus = responseJson
            .RootElement.GetProperty("instance")
            .GetProperty("status");
        Assert.True(responseStatus.GetProperty("isArchived").GetBoolean());
        Assert.Equal(ended, responseStatus.GetProperty("archived").GetDateTime());
        Assert.True(storedInstance.Status.IsArchived);
        Assert.Equal(ended, storedInstance.Status.Archived);
        if (withExistingStatus)
        {
            Assert.Equal("Read", responseStatus.GetProperty("readStatus").GetString());
            Assert.Equal(ReadStatus.Read, storedInstance.Status.ReadStatus);
        }

        Assert.Equal(ended, storedInstance.Process.Ended);
        Assert.NotNull(capturedMutation);
        Assert.Equal(
            1,
            capturedMutation.InstanceUpdateProperties.Count(property =>
                property == nameof(InstanceInternal.Status)
            )
        );
        Assert.Contains(
            nameof(InstanceStatus.IsArchived),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.Contains(nameof(InstanceStatus.Archived), capturedMutation.InstanceUpdateProperties);
    }

    [Fact]
    public async Task CommitMutation_NonEndedProcess_DoesNotTouchStatus()
    {
        // Arrange
        InstanceStatus initialStatus = new() { ReadStatus = ReadStatus.Read };
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            initialStatus
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_2" },
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(initialStatus, storedInstance.Status);
        Assert.False(storedInstance.Status.IsArchived);
        Assert.Null(storedInstance.Status.Archived);
        Assert.NotNull(capturedMutation);
        Assert.Null(capturedMutation.InstanceUpdates.Status);
        Assert.DoesNotContain(
            nameof(InstanceInternal.Status),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.DoesNotContain(
            nameof(InstanceStatus.IsArchived),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.DoesNotContain(
            nameof(InstanceStatus.Archived),
            capturedMutation.InstanceUpdateProperties
        );
    }

    [Fact]
    public async Task CommitMutation_AlreadyEndedStoredProcess_ReturnsForbidden()
    {
        // Arrange
        DateTime storedEnded = new(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc);
        DateTime storedArchived = new(2026, 7, 10, 8, 31, 0, DateTimeKind.Utc);
        DateTime incomingEnded = new(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        InstanceStatus initialStatus = new()
        {
            IsArchived = true,
            Archived = storedArchived,
            ReadStatus = ReadStatus.Read,
        };
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                Ended = storedEnded,
                EndEvent = "EndEvent_1",
            },
            initialStatus
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    Ended = incomingEnded,
                    EndEvent = "EndEvent_2",
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        // An ended process has no current task, so process-state mutations are rejected
        // (parity with the process controller); the stored instance stays untouched.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Same(initialStatus, storedInstance.Status);
        Assert.True(storedInstance.Status.IsArchived);
        Assert.Equal(storedArchived, storedInstance.Status.Archived);
        Assert.Equal(storedEnded, storedInstance.Process.Ended);
        Assert.Null(capturedMutation);
    }

    [Fact]
    public async Task CommitMutation_AddCompleteConfirmation_ConfirmsForTheCallingOrg()
    {
        // Arrange
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            null
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            AddCompleteConfirmation = true,
            DataValues = new Dictionary<string, string>
            {
                ["eFormidlingShipmentStatus"] = "levert",
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument responseJson = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()
        );
        JsonElement responseConfirmation = Assert.Single(
            responseJson
                .RootElement.GetProperty("instance")
                .GetProperty("completeConfirmations")
                .EnumerateArray()
        );
        Assert.Equal("ttd", responseConfirmation.GetProperty("stakeholderId").GetString());
        CompleteConfirmation storedConfirmation = Assert.Single(
            storedInstance.CompleteConfirmations
        );
        Assert.Equal("ttd", storedConfirmation.StakeholderId);
        Assert.NotNull(capturedMutation);
        Assert.Contains(
            nameof(InstanceInternal.CompleteConfirmations),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.Single(
            capturedMutation.InstanceEvents,
            instanceEvent =>
                instanceEvent.EventType == InstanceEventType.ConfirmedComplete.ToString()
        );
    }

    private static string ExpectedDuplicateDataElementMutationIdsResponse(Guid dataElementId) =>
        $"\"dataElementId '{dataElementId}' is referenced by more than one operation.\"";

    private static InstanceInternal CreateMutationInstance(
        ProcessState process,
        InstanceStatus status
    )
    {
        Instance instance = new()
        {
            Id = $"{SensitiveDataApp.InstanceOwnerPartyId}/{SensitiveDataApp.InstanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = SensitiveDataApp.InstanceOwnerPartyId },
            AppId = "ttd/sensitive-data",
            Org = "ttd",
            Process = process,
            Status = status,
            Data = [],
        };

        return InstanceInternalTestFactory.Create(instance, [], InternalId: 1);
    }

    private static Mock<IInstanceRepository> CreateMutationInstanceRepository(
        InstanceInternal storedInstance
    )
    {
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    true,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(storedInstance);
        return repositoryMock;
    }

    private static Mock<IInstanceMutationRepository> CreatePersistingMutationRepository(
        InstanceInternal storedInstance,
        Action<InstanceMutationCommit> captureMutation
    )
    {
        Mock<IInstanceMutationRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.Apply(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    storedInstance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    captureMutation(mutation);
                    if (mutation.InstanceUpdateProperties.Contains(nameof(InstanceInternal.Status)))
                    {
                        storedInstance.Status = mutation.InstanceUpdates.Status;
                    }

                    if (
                        mutation.InstanceUpdateProperties.Contains(nameof(InstanceInternal.Process))
                    )
                    {
                        storedInstance.Process = mutation.InstanceUpdates.Process;
                    }

                    if (
                        mutation.InstanceUpdateProperties.Contains(
                            nameof(InstanceInternal.CompleteConfirmations)
                        )
                    )
                    {
                        storedInstance.CompleteConfirmations =
                        [
                            .. storedInstance.CompleteConfirmations ?? [],
                            .. mutation.InstanceUpdates.CompleteConfirmations,
                        ];
                    }

                    return new InstanceMutationApplyResult(false, [], storedInstance);
                }
            );
        return repositoryMock;
    }

    private HttpClient GetTestClient(
        string bearerAuthToken = null,
        Mock<IInstanceMutationRepository> mutationRepositoryMock = null,
        Mock<IInstanceRepository> instanceRepositoryMock = null
    )
    {
        if (mutationRepositoryMock is null)
        {
            mutationRepositoryMock = new Mock<IInstanceMutationRepository>();
            mutationRepositoryMock
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
                        new InstanceInternal { Versions = new StorageVersions(2, 1) }
                    )
                );
        }

        // No setup required for these services. They are not in use by the InstanceController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new();
        Mock<IPartiesWithInstancesClient> partiesWrapper = new();
        Mock<IMessageBus> busMock = new();

        var webApplicationFactory = factory.WithWebHostBuilder(builder =>
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                .Build();
            builder.ConfigureAppConfiguration(
                (hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                }
            );

            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();

                services.AddSingleton(mutationRepositoryMock.Object);

                if (instanceRepositoryMock is not null)
                {
                    services.AddSingleton(instanceRepositoryMock.Object);
                }

                services.AddSingleton<
                    IPostConfigureOptions<JwtCookieOptions>,
                    JwtCookiePostConfigureOptionsStub
                >();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();

                services.AddSingleton(keyVaultWrapper.Object);
                services.AddSingleton(partiesWrapper.Object);
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                services.AddSingleton(busMock.Object);
            });
        });

        var client = webApplicationFactory.CreateClient();
        if (!string.IsNullOrEmpty(bearerAuthToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                bearerAuthToken
            );
        }

        return client;
    }

    private static class SensitiveDataApp
    {
        public const string InstanceGuid = "99194777-a691-433a-ace1-225e9a691653";
        public const string InstanceOwnerPartyId = "1337";

        public static class DataTypes
        {
            public const string Default = "model";
            public const string SensitiveRead = "sensitive-data-read";
            public const string SensitiveWrite = "sensitive-data-write";
            public const string SensitiveBoth = "sensitive-data-both";
        }

        public static class DataElements
        {
            public const string Default = "70d122f8-0cae-44f4-8cd5-2887c251a959";
            public const string SensitiveRead = "15c0fa5d-a243-4fa2-882b-002bb60b6227";
            public const string SensitiveWrite = "6448a556-2db0-4279-b535-13e7f9c05809";
            public const string SensitiveBoth = "bb64df50-fdb1-456b-943e-9c32f524943e";
        }

        public static string GetInstanceUrl() =>
            $"{_versionPrefix}/instances/{InstanceOwnerPartyId}/{InstanceGuid}";
    }
}
