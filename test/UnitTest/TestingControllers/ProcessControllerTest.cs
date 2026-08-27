using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Test class for Process Controller. Focuses on authorization of requests.
/// </summary>
public class ProcessControllerTest : IClassFixture<TestApplicationFactory<ProcessController>>
{
    private readonly TestApplicationFactory<ProcessController> _factory;

    public ProcessControllerTest(TestApplicationFactory<ProcessController> factory)
    {
        _factory = factory;
    }

    private async Task<HttpResponseMessage> SendUpdateRequest(
        bool useInstanceAndEventsEndpoint,
        string token,
        string? instanceId = null,
        IInstanceRepository? instanceRepository = null,
        IInstanceMutationRepository? instanceMutationRepository = null,
        IProcessDataCleanupService? processDataCleanupService = null,
        IDataService? dataService = null,
        IApplicationService? applicationService = null,
        IProcessAuthorizer? processAuthorizer = null,
        Action<ProcessState>? configure = null,
        string? deleteGeneratedElements = null,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null,
        string? rawExpectedInstanceVersion = null,
        Action<ProcessStateUpdate>? configureUpdate = null
    )
    {
        instanceId ??= "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"storage/api/v1/instances/{instanceId}/process/";
        JsonContent jsonString;
        if (useInstanceAndEventsEndpoint)
        {
            requestUri += "instanceandevents/";
            ProcessStateUpdate update = new();
            ProcessState state = update.State = new();
            configure?.Invoke(state);
            configureUpdate?.Invoke(update);
            jsonString = JsonContent.Create(update, new MediaTypeHeaderValue("application/json"));
        }
        else
        {
            ProcessState state = new();
            configure?.Invoke(state);
            jsonString = JsonContent.Create(state, new MediaTypeHeaderValue("application/json"));
        }

        // Passed verbatim so tests can exercise non-boolean values that model binding must reject.
        if (deleteGeneratedElements is not null)
        {
            requestUri += $"?deleteGeneratedElements={deleteGeneratedElements}";
        }

        HttpClient client = GetTestClient(
            instanceRepository,
            instanceMutationRepository,
            processDataCleanupService: processDataCleanupService,
            dataService: dataService,
            applicationService: applicationService,
            processAuthorizer: processAuthorizer
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // rawExpectedInstanceVersion carries values the version precondition parser must reject,
        // so it is sent verbatim. It sets the same header as expectedInstanceVersion, and the two
        // are resolved here so the header can never be sent twice.
        string? instanceVersionHeader =
            rawExpectedInstanceVersion
            ?? expectedInstanceVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (instanceVersionHeader is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                StorageHeaders.IfInstanceVersionMatch,
                instanceVersionHeader
            );
        }

        if (expectedProcessStateVersion is not null)
        {
            client.DefaultRequestHeaders.Add(
                StorageHeaders.IfProcessStateVersionMatch,
                expectedProcessStateVersion.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            );
        }

        // Act
        return await client.PutAsync(requestUri, jsonString);
    }

    public static TheoryData<bool> UpdateTestParameters => new() { { true }, { false } };

    public static TheoryData<string> MalformedVersionPreconditions =>
        ["0", "-1", "1.0", "not-a-version", "+5", " 5 ", "5,5"];

    /// <summary>
    /// Version preconditions are parsed strictly: only a plain positive 32-bit integer is
    /// accepted. Exercised over HTTP so model binding is covered alongside the parser.
    /// </summary>
    [Theory]
    [MemberData(nameof(MalformedVersionPreconditions))]
    public async Task PutProcess_MalformedInstanceVersionPrecondition_ReturnsBadRequest(
        string rawExpectedInstanceVersion
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: false,
            token: token,
            rawExpectedInstanceVersion: rawExpectedInstanceVersion
        );

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemDetails? problem = JsonConvert.DeserializeObject<ProblemDetails>(
            await response.Content.ReadAsStringAsync()
        );
        Assert.Equal("malformed_version_precondition", problem?.Type);
    }

    /// <summary>
    /// A whitespace-only value is not rejected: model binding reports an empty header value as
    /// null, so it cannot be told apart from an absent header and means "no precondition".
    /// </summary>
    [Theory]
    [InlineData("5")]
    [InlineData("   ")]
    public async Task PutProcess_InstanceVersionPrecondition_IsNotRejectedByParsing(string value)
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: false,
            token: token,
            rawExpectedInstanceVersion: value
        );

        // Assert
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task GetProcessHistory_UserHasToLowAuthLv_ReturnStatusForbidden()
    {
        // Arrange
        string requestUri =
            $"storage/api/v1/instances/1337/ba577e7f-3dfd-4ff6-b659-350308a47348/process/history";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: Response is deny.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task GetProcessHistory_ReponseIsDeny_ReturnStatusForbidden()
    { // Arrange
        string requestUri =
            $"storage/api/v1/instances/1337/ba577e7f-3dfd-4ff6-b659-350308a47348/process/history";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(-1, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is authorized.
    /// Expected: Success status code. Empty process history is returned
    /// </summary>
    [Fact]
    public async Task GetProcessHistory_UserIsAuthorized_ReturnsEmptyProcessHistoryReturnStatusForbidden()
    {
        // Arrange
        string requestUri =
            $"storage/api/v1/instances/1337/17ad1851-f6cb-4573-bfcb-a17d145307b3/process/history";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseString = await response.Content.ReadAsStringAsync();
        ProcessHistoryList processHistory =
            JsonConvert.DeserializeObject<ProcessHistoryList>(responseString)
            ?? throw new Exception("Failed to deserialize response content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(processHistory.ProcessHistory);
    }

    /// <summary>
    /// Test case: The instance lacks process data.
    /// Expected: Forbidden status code
    /// </summary>
    [Fact]
    public async Task PutInstanceEvents_WhenProcessMissingInExistingInstance_ReturnsStatusForbidden()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 1);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/67f568ce-f114-48e7-ba12-dd422f73667a"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserHasToLowAuthLv_ReturnStatusForbidden(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 1);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/ae3fe2fa-1fcb-42b4-8e63-69a42d4e3502"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: Response is deny.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_PDPResponseIsDeny_ReturnStatusForbidden(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(-1, 1);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/ae3fe2fa-1fcb-42b4-8e63-69a42d4e3502"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserIsAuthorized_ReturnStatusOK(bool useInstanceAndEventsEndpoint)
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 7)]
    [InlineData(true, null)]
    [InlineData(true, 7)]
    public async Task PutProcess_WriteUsesSnapshotProcessStateVersionAndPreservesClientInstanceVersion(
        bool useInstanceAndEventsEndpoint,
        int? expectedInstanceVersion
    )
    {
        // Arrange
        const int instanceVersion = 7;
        const int processStateVersion = 11;
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(instanceVersion, processStateVersion)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        repositoryMock
            .Setup(repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(snapshot);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceMutationApplyResult(false, [], snapshot));
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object,
            expectedInstanceVersion: expectedInstanceVersion
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Times expectedUpdateCalls = useInstanceAndEventsEndpoint ? Times.Never() : Times.Once();
        repositoryMock.Verify(
            repository =>
                repository.Update(
                    snapshot,
                    It.IsAny<List<string>>(),
                    It.Is<int?>(value => value == expectedInstanceVersion),
                    processStateVersion,
                    It.IsAny<CancellationToken>()
                ),
            expectedUpdateCalls
        );
        Times expectedApplyCalls = useInstanceAndEventsEndpoint ? Times.Once() : Times.Never();
        mutationRepositoryMock.Verify(
            repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.Is<InstanceMutationCommit>(mutation =>
                        mutation.ExpectedInstanceVersion == expectedInstanceVersion
                        && mutation.ExpectedProcessStateVersion == processStateVersion
                    ),
                    It.IsAny<CancellationToken>()
                ),
            expectedApplyCalls
        );
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task PutProcess_MismatchingClientVersionFastFailsBeforeAuthorizationAndWrite(
        bool useInstanceAndEventsEndpoint,
        bool instanceVersionMismatch,
        bool processStateVersionMismatch
    )
    {
        // Arrange
        const int instanceVersion = 7;
        const int processStateVersion = 11;
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(instanceVersion, processStateVersion)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        Mock<IProcessAuthorizer> processAuthorizerMock = new();

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object,
            expectedInstanceVersion: instanceVersionMismatch ? instanceVersion - 1 : null,
            expectedProcessStateVersion: processStateVersionMismatch
                ? processStateVersion - 1
                : null
        );

        // Assert
        await AssertVersionMismatchResponse(
            response,
            instanceVersionMismatch
                ? "instance_version_mismatch"
                : "process_state_version_mismatch",
            instanceVersion,
            processStateVersion
        );
        processAuthorizerMock.Verify(
            authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                ),
            Times.Never
        );
        repositoryMock.Verify(
            repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        mutationRepositoryMock.Verify(
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task PutProcess_WriteTimeProcessStateVersionMismatchReturnsExistingProblemDetails(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        const int currentInstanceVersion = 9;
        const int currentProcessStateVersion = 12;
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        ProcessStateVersionMismatchException exception = new(
            currentInstanceVersion,
            currentProcessStateVersion
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        repositoryMock
            .Setup(repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(exception);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(exception);
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object
        );

        // Assert
        await AssertVersionMismatchResponse(
            response,
            "process_state_version_mismatch",
            currentInstanceVersion,
            currentProcessStateVersion
        );
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 11)]
    [InlineData(true, null)]
    [InlineData(true, 11)]
    public async Task PutProcess_ProcessingStatusReachesWriteWithOrWithoutClientProcessStateVersion(
        bool useInstanceAndEventsEndpoint,
        int? expectedProcessStateVersion
    )
    {
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        snapshot.Process ??= new ProcessState();
        snapshot.Process.Status = ProcessStatus.Idle;
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        repositoryMock
            .Setup(repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (InstanceInternal instance, List<string> _, int? _, int? _, CancellationToken _) =>
                    instance
            );
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object,
            configure: state => state.Status = ProcessStatus.Processing,
            expectedProcessStateVersion: expectedProcessStateVersion
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Instance responseInstance =
            JsonConvert.DeserializeObject<Instance>(await response.Content.ReadAsStringAsync())
            ?? throw new Exception("Failed to deserialize response instance");
        Assert.Equal(ProcessStatus.Processing, responseInstance.Process.Status);
        repositoryMock.Verify(
            repository =>
                repository.Update(
                    It.Is<InstanceInternal>(instance =>
                        instance.Process.Status == ProcessStatus.Processing
                    ),
                    It.Is<List<string>>(properties =>
                        properties.Contains(nameof(InstanceInternal.Process))
                    ),
                    null,
                    11,
                    It.IsAny<CancellationToken>()
                ),
            useInstanceAndEventsEndpoint ? Times.Never() : Times.Once()
        );
        mutationRepositoryMock.Verify(
            repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.Is<InstanceMutationCommit>(mutation =>
                        mutation.InstanceUpdates.Process.Status == ProcessStatus.Processing
                        && mutation.ExpectedProcessStateVersion == 11
                    ),
                    It.IsAny<CancellationToken>()
                ),
            useInstanceAndEventsEndpoint ? Times.Once() : Times.Never()
        );
    }

    [Fact]
    public async Task PutInstanceAndEvents_ClientTimestampsWithoutOffset_ReachRepositoryUnshifted()
    {
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        DateTime clientTimestamp = new(2026, 5, 6, 7, 8, 9, DateTimeKind.Unspecified);
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        InstanceMutationCommit? capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    capturedMutation = mutation;
                    return new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates);
                }
            );
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object,
            configure: state => state.Started = clientTimestamp,
            configureUpdate: update =>
                update.Events = [
                    new InstanceEvent
                    {
                        InstanceId = $"1337/{instanceGuid}",
                        EventType = InstanceEventType.Saved.ToString(),
                        Created = clientTimestamp,
                        User = new PlatformUser { UserId = 3 },
                    },
                ]
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedMutation);
        DateTime? capturedStarted = capturedMutation.InstanceUpdates.Process.Started;
        Assert.Equal(clientTimestamp, capturedStarted);
        Assert.Equal(DateTimeKind.Unspecified, capturedStarted!.Value.Kind);
        InstanceEvent capturedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(clientTimestamp, capturedEvent.Created);
        Assert.Equal(DateTimeKind.Unspecified, capturedEvent.Created!.Value.Kind);
    }

    [Fact]
    public async Task PutInstanceAndEvents_EventWithoutCreated_GetsServerAssignedUtcTimestamp()
    {
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        InstanceMutationCommit? capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    instanceGuid,
                    snapshot.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    capturedMutation = mutation;
                    return new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates);
                }
            );
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);
        DateTime before = DateTime.UtcNow;

        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object,
            configureUpdate: update =>
                update.Events = [
                    new InstanceEvent
                    {
                        InstanceId = $"1337/{instanceGuid}",
                        EventType = InstanceEventType.Saved.ToString(),
                        User = new PlatformUser { UserId = 3 },
                    },
                ]
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedMutation);
        InstanceEvent capturedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(DateTimeKind.Utc, capturedEvent.Created!.Value.Kind);
        Assert.InRange(capturedEvent.Created.Value, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PutProcess_ProcessingStatusWithStaleProcessStateVersionFence_ReturnsPreconditionFailed(
        bool useInstanceAndEventsEndpoint
    )
    {
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();

        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            configure: state => state.Status = ProcessStatus.Processing,
            expectedProcessStateVersion: 10
        );

        await AssertVersionMismatchResponse(
            response,
            "process_state_version_mismatch",
            currentInstanceVersion: 7,
            currentProcessStateVersion: 11
        );
        mutationRepositoryMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PutProcess_UnknownStatus_ReturnsBadRequestBeforeRead(
        bool useInstanceAndEventsEndpoint
    )
    {
        Mock<IInstanceRepository> repositoryMock = new();
        string requestUri =
            "storage/api/v1/instances/1337/20b1353e-91cf-44d6-8ff7-f68993638ffe/process/";
        string body = """{"status":"future-status"}""";
        if (useInstanceAndEventsEndpoint)
        {
            requestUri += "instanceandevents/";
            body = """{"state":{"status":"future-status"}}""";
        }

        HttpClient client = GetTestClient(repositoryMock.Object);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        using HttpResponseMessage response = await client.PutAsync(
            requestUri,
            new StringContent(body, Encoding.UTF8, "application/json")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repositoryMock.Verify(
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
    public async Task PutInstanceAndEvents_DefaultExpectedStatusWhileProcessing_ReturnsConflictBeforeApply()
    {
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        snapshot.Process ??= new ProcessState();
        snapshot.Process.Status = ProcessStatus.Processing;
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();

        using HttpResponseMessage response = await SendUpdateRequest(
            true,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "processing",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal
        );
        mutationRepositoryMock.Verify(
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task PutProcess_ProcessStatusConflictReturnsConflictWithCurrentStatus(
        bool useInstanceAndEventsEndpoint
    )
    {
        const ProcessStatus currentStatus = ProcessStatus.Processing;
        Guid instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
        InstanceInternal snapshot = CreateVersionedInstanceSnapshot(
            instanceGuid,
            new StorageVersions(7, 11)
        );
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(instanceGuid, true, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(snapshot);
        repositoryMock
            .Setup(repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(currentStatus));
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(currentStatus));
        Mock<IProcessAuthorizer> processAuthorizerMock = new();
        processAuthorizerMock
            .Setup(authorizer =>
                authorizer.AuthorizeProcessNext(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<ProcessState>()
                )
            )
            .ReturnsAsync(true);

        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            PrincipalUtil.GetToken(3, 1337, 3),
            $"1337/{instanceGuid}",
            repositoryMock.Object,
            mutationRepositoryMock.Object,
            processAuthorizer: processAuthorizerMock.Object
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            currentStatus.ToString().ToLowerInvariant(),
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserIsAuthorized_Signing_OnlyHasSignRights_ReturnsStatusOK(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        testInstance.Process.CurrentTask = new ProcessElementInfo()
        {
            ElementId = "Task_2",
            AltinnTaskType = "signing",
            FlowType = "CompleteCurrentMoveToNext",
        };

        var instanceRepoMock = new Mock<IInstanceRepository>();
        instanceRepoMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: testInstance.Id,
            instanceRepository: instanceRepoMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_3",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserIsAuthorized_Signing_OnlyHasWriteRights_ReturnsStatusOK(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        testInstance.Process.CurrentTask = new ProcessElementInfo()
        {
            ElementId = "Task_3",
            AltinnTaskType = "signing",
            FlowType = "CompleteCurrentMoveToNext",
        };

        var instanceRepoMock = new Mock<IInstanceRepository>();
        instanceRepoMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: testInstance.Id,
            instanceRepository: instanceRepoMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_4",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserIsAuthorized_Payment_OnlyHasWriteRights_ReturnsStatusOK(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        testInstance.Process.CurrentTask = new ProcessElementInfo()
        {
            ElementId = "Task_3",
            AltinnTaskType = "payment",
            FlowType = "CompleteCurrentMoveToNext",
        };

        var instanceRepoMock = new Mock<IInstanceRepository>();
        instanceRepoMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: testInstance.Id,
            instanceRepository: instanceRepoMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_4",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_UserIsAuthorized_CustomTaskType_OnlyHasWriteRights_ReturnsStatusOK(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        testInstance.Process.CurrentTask = new ProcessElementInfo()
        {
            ElementId = "Task_4",
            AltinnTaskType = "custom-task-type",
            FlowType = "CompleteCurrentMoveToNext",
        };

        var instanceRepoMock = new Mock<IInstanceRepository>();
        instanceRepoMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: testInstance.Id,
            instanceRepository: instanceRepoMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_5",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: Uses want to go back to a earlier state
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcessGatewayReturn_UserIsAuthorized_ReturnStatusOK(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe",
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    FlowType = "AbandonCurrentReturnToNext",
                    AltinnTaskType = "data",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: User wants to updates process on confirimation task. User does not have role required
    /// Expected: Returns forbidden.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcessConfirm_UserIsNotAuthorized_ReturnDenied(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User is Authorized
    /// Expected: Returns status ok.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_EndProcess_EnsureArchivedStateIsSet(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        Mock<IInstanceRepository> repositoryMock = new Mock<IInstanceRepository>();
        repositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        repositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (InstanceInternal i, List<string> _, int? _, int? _, CancellationToken _) => i
            );

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/377efa97-80ee-4cc6-8d48-09de12cc273d",
            instanceRepository: repositoryMock.Object,
            configure: state =>
            {
                state.Started = DateTime.Parse("2020-04-29T13:53:01.7020218Z");
                state.StartEvent = "StartEvent_1";
                state.Ended = DateTime.UtcNow;
                state.EndEvent = "EndEvent_1";
            }
        );

        // Assert
        string responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Instance actual =
            JsonConvert.DeserializeObject<Instance>(responseContent)
            ?? throw new Exception("Failed to deserialize response content");
        Assert.True(actual.Status.IsArchived);
        repositoryMock.Verify(
            ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    /// <summary>
    /// Test case: User pushes process to signing step.
    /// Expected: An instance event of type "sentToSign" is registered.
    /// </summary>
    [Theory]
    [MemberData(nameof(UpdateTestParameters))]
    public async Task PutProcess_MoveToSigning_SentToSignEventGenerated(
        bool useInstanceAndEventsEndpoint
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "signing",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test case: User advances into a task. PutInstanceAndEvents should invoke
    /// the cleanup service with the incoming CurrentTask.ElementId.
    /// </summary>
    [Fact]
    public async Task PutInstanceAndEvents_AdvancingIntoTask_InvokesCleanupWithDestinationTaskId()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c =>
                c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .Returns([]);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cleanupMock.Verify(
            c => c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), "Task_2"),
            Times.Once
        );
    }

    [Fact]
    public async Task PutInstanceAndEvents_GeneratedDataCleanup_CleansDeletedBlobsAfterCommit()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        DataElementInternal staleDataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
        }.FromApiModel("old-version");
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c => c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), "Task_2"))
            .Returns([staleDataElement]);

        bool committed = false;
        InstanceMutationCommit? capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(r =>
                r.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) =>
                {
                    capturedMutation = mutation;
                    committed = true;
                }
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );

        Mock<IDataService> dataServiceMock = new();
        dataServiceMock
            .Setup(d =>
                d.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    staleDataElement,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.True(committed))
            .Returns(Task.CompletedTask);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            instanceMutationRepository: mutationRepositoryMock.Object,
            processDataCleanupService: cleanupMock.Object,
            dataService: dataServiceMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedMutation);
        Assert.Contains(
            capturedMutation.InstanceEvents,
            instanceEvent =>
                instanceEvent.EventType == InstanceEventType.Deleted.ToString()
                && instanceEvent.DataId == staleDataElement.Id.ToString()
        );
        dataServiceMock.Verify(
            d =>
                d.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    staleDataElement,
                    null,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task PutInstanceAndEvents_PostCommitBlobCleanupThrows_Returns500WithoutVersionHeaders()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        DataElementInternal staleDataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
        }.FromApiModel("old-version");
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c => c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), "Task_2"))
            .Returns([staleDataElement]);

        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(r =>
                r.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                    new InstanceMutationApplyResult(false, [], mutation.InstanceUpdates)
            );

        Mock<IDataService> dataServiceMock = new();
        dataServiceMock
            .Setup(d =>
                d.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    staleDataElement,
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("physical blob cleanup failed"));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            instanceMutationRepository: mutationRepositoryMock.Object,
            processDataCleanupService: cleanupMock.Object,
            dataService: dataServiceMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains(StorageHeaders.InstanceVersion));
        Assert.False(response.Headers.Contains(StorageHeaders.ProcessStateVersion));
    }

    [Fact]
    public async Task PutInstanceAndEvents_ResponseIsBuiltFromApplySnapshot()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        DateTime snapshotLastChanged = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .Setup(r =>
                r.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    InstanceInternal snapshot = mutation
                        .InstanceUpdates.ToApiModel()
                        .FromApiModel();
                    snapshot.LastChanged = snapshotLastChanged;
                    snapshot.Versions = new StorageVersions(9, 12);
                    return new InstanceMutationApplyResult(false, [], snapshot);
                }
            );

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            instanceMutationRepository: mutationRepositoryMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string responseContent = await response.Content.ReadAsStringAsync();
        Instance actual =
            JsonConvert.DeserializeObject<Instance>(responseContent)
            ?? throw new Exception("Failed to deserialize response content");
        Assert.NotNull(actual.LastChanged);
        Assert.Equal(snapshotLastChanged, actual.LastChanged.Value.ToUniversalTime());
        Assert.Equal(
            "9",
            Assert.Single(response.Headers.GetValues(StorageHeaders.InstanceVersion))
        );
        Assert.Equal(
            "12",
            Assert.Single(response.Headers.GetValues(StorageHeaders.ProcessStateVersion))
        );
    }

    [Fact]
    public async Task PutInstanceAndEvents_PostCommitApplicationLookupThrows_StillReturnsOkAndRunsCleanup()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        DataElementInternal staleDataElement = new DataElement
        {
            Id = Guid.NewGuid().ToString(),
        }.FromApiModel("old-version");
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c => c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), "Task_2"))
            .Returns([staleDataElement]);

        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock
            .Setup(a => a.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed"));

        Mock<IDataService> dataServiceMock = new();
        dataServiceMock
            .Setup(d =>
                d.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    staleDataElement,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            dataService: dataServiceMock.Object,
            applicationService: applicationServiceMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        dataServiceMock.Verify(
            d =>
                d.CleanupDeletedDataElementBlobs(
                    It.IsAny<InstanceInternal>(),
                    staleDataElement,
                    null,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    /// <summary>
    /// Test case: Caller advances into a task but opts out with deleteGeneratedElements=false, declaring
    /// it manages its own task-generated data. Cleanup must not be invoked.
    /// </summary>
    [Fact]
    public async Task PutInstanceAndEvents_DeleteGeneratedElementsFalse_DoesNotInvokeCleanup()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c =>
                c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .Returns([]);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            },
            deleteGeneratedElements: "false"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cleanupMock.Verify(
            c =>
                c.GetGeneratedFromTaskDataElements(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    /// <summary>
    /// Test case: Caller advances into a task and either omits deleteGeneratedElements or sends it as an
    /// explicit true (case-insensitive). The default is to clean, and true asks for cleaning, so cleanup
    /// must run in both cases - this guards the opt-out polarity (absent/true -> clean).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("True")]
    public async Task PutInstanceAndEvents_DeleteGeneratedElementsAbsentOrTrue_InvokesCleanup(
        string? queryValue
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c =>
                c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .Returns([]);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            },
            deleteGeneratedElements: queryValue
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cleanupMock.Verify(
            c => c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), "Task_2"),
            Times.Once
        );
    }

    /// <summary>
    /// Test case: Caller sends deleteGeneratedElements with a value that is not a boolean. The parameter is
    /// bound as a nullable bool, so model binding rejects it with 400 before the action runs, and cleanup
    /// is never reached. A caller sending this deliberate parameter is expected to encode a valid boolean.
    /// </summary>
    [Theory]
    [InlineData("not-a-bool")]
    [InlineData("maybe")]
    public async Task PutInstanceAndEvents_DeleteGeneratedElementsNotBoolean_ReturnsBadRequest(
        string queryValue
    )
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c =>
                c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .Returns([]);

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            },
            deleteGeneratedElements: queryValue
        );

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        cleanupMock.Verify(
            c =>
                c.GetGeneratedFromTaskDataElements(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    /// <summary>
    /// Test case: Terminal transition (no CurrentTask). Cleanup must be skipped to
    /// avoid wiping data elements that should be preserved post-process.
    /// </summary>
    [Fact]
    public async Task PutInstanceAndEvents_TerminalTransition_DoesNotInvokeCleanup()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Instance testInstance = TestDataUtil.GetInstance(
            new Guid("377efa97-80ee-4cc6-8d48-09de12cc273d")
        );
        testInstance.Id = $"{testInstance.InstanceOwner.PartyId}/{testInstance.Id}";

        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(testInstance, [], InternalId: 0));
        repositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    1,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (InstanceInternal i, List<string> _, int? _, int? _, CancellationToken _) => i
            );
        Mock<IProcessDataCleanupService> cleanupMock = new();

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/377efa97-80ee-4cc6-8d48-09de12cc273d",
            instanceRepository: repositoryMock.Object,
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.Started = DateTime.Parse("2020-04-29T13:53:01.7020218Z");
                state.StartEvent = "StartEvent_1";
                state.Ended = DateTime.UtcNow;
                state.EndEvent = "EndEvent_1";
                // No CurrentTask — terminal transition.
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cleanupMock.Verify(
            c =>
                c.GetGeneratedFromTaskDataElements(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    /// <summary>
    /// Test case: Process authorizer denies the request. Cleanup must not run before
    /// the Forbid() short-circuit, otherwise we would delete data on an unauthorized
    /// request.
    /// </summary>
    [Fact]
    public async Task PutInstanceAndEvents_Unauthorized_DoesNotInvokeCleanup()
    {
        // Arrange — auth level 1 is below the level required for the test instance.
        string token = PrincipalUtil.GetToken(3, 1337, 1);
        Mock<IProcessDataCleanupService> cleanupMock = new();

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/ae3fe2fa-1fcb-42b4-8e63-69a42d4e3502",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        cleanupMock.Verify(
            c =>
                c.GetGeneratedFromTaskDataElements(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    /// <summary>
    /// Test case: Cleanup service throws unexpectedly (defensive — the service
    /// itself swallows per-element failures, but if the service contract is
    /// violated and an exception escapes, that should NOT be silently absorbed
    /// here either: the process advance fails so the caller can retry. This
    /// codifies the contract: cleanup-service exceptions surface as 500s.
    /// </summary>
    [Fact]
    public async Task PutInstanceAndEvents_CleanupServiceThrows_BubblesUp()
    {
        // Arrange
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        Mock<IProcessDataCleanupService> cleanupMock = new();
        cleanupMock
            .Setup(c =>
                c.GetGeneratedFromTaskDataElements(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .Throws(new InvalidOperationException("contract violation"));

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint: true,
            token: token,
            instanceId: "1337/20a1353e-91cf-44d6-8ff7-f68993638ffe",
            processDataCleanupService: cleanupMock.Object,
            configure: state =>
            {
                state.CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_2",
                    AltinnTaskType = "data",
                    FlowType = "CompleteCurrentMoveToNext",
                };
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Theory]
    [InlineData("data", new[] { "write" })]
    [InlineData("feedback", new[] { "write" })]
    [InlineData("pdf", new[] { "write" })]
    [InlineData("eFormidling", new[] { "write" })]
    [InlineData("fiksArkiv", new[] { "write" })]
    [InlineData("subformPdf", new[] { "write" })]
    [InlineData("payment", new[] { "pay", "write" })]
    [InlineData("confirmation", new[] { "confirm" })]
    [InlineData("signing", new[] { "sign", "write" })]
    [InlineData("customTask", new[] { "customTask" })]
    public void GetActionsThatAllowProcessNextForTaskType_ReturnsExpectedActions(
        string taskType,
        string[] expectedActions
    )
    {
        // Act
        List<string> result = ProcessAuthorizer.GetActionsThatAllowProcessNextForTaskType(taskType);

        // Assert
        Assert.Equal(expectedActions, result);
    }

    [Theory]
    [InlineData(123, null, null, null)]
    [InlineData(null, "someOrg", null, null)]
    [InlineData(null, null, "someSystemUserOwnerOrgNo", null)]
    [InlineData(null, null, null, 123)]
    public void ValidateInstanceEventUserObject_ReturnsTrueForValidUserObject(
        int? userId,
        string? orgId,
        string? systemUserOwnerOrgNo,
        int? endUserSystemId
    )
    {
        // Arrange
        Guid? systemUserId = null;
        if (systemUserOwnerOrgNo is not null)
        {
            systemUserId = new Guid("00000000-0000-0000-0000-000000000000");
        }
        // Act
        bool result = ProcessController.ValidateInstanceEventUserObject(
            userId,
            orgId,
            systemUserId,
            systemUserOwnerOrgNo,
            endUserSystemId
        );

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateInstanceEventUserObject_ReturnsFalseWhenMissingSystemUerIdForSystemUser()
    {
        // Act
        bool result = ProcessController.ValidateInstanceEventUserObject(
            null,
            null,
            null,
            "someSystemUserOwnerOrgNo",
            null
        );

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateInstanceEventUserObject_ReturnsFalseWhenMissingPartialSystemUser()
    {
        // Act
        bool result = ProcessController.ValidateInstanceEventUserObject(
            null,
            null,
            Guid.NewGuid(),
            null,
            null
        );

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateInstanceEventUserObject_ReturnsFalseWhenAllParametersAreNull()
    {
        // Act
        bool result = ProcessController.ValidateInstanceEventUserObject(
            null,
            null,
            null,
            null,
            null
        );

        // Assert
        Assert.False(result);
    }

    private static InstanceInternal CreateVersionedInstanceSnapshot(
        Guid instanceGuid,
        StorageVersions versions
    )
    {
        Instance instance = TestDataUtil.GetInstance(instanceGuid);
        instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";
        return InstanceInternalTestFactory.Create(instance, [], InternalId: 42, versions: versions);
    }

    private static async Task AssertVersionMismatchResponse(
        HttpResponseMessage response,
        string expectedProblemType,
        int currentInstanceVersion,
        int currentProcessStateVersion
    )
    {
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(
            currentInstanceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(response.Headers.GetValues(StorageHeaders.InstanceVersion))
        );
        Assert.Equal(
            currentProcessStateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(response.Headers.GetValues(StorageHeaders.ProcessStateVersion))
        );
        ProblemDetails problem =
            JsonConvert.DeserializeObject<ProblemDetails>(
                await response.Content.ReadAsStringAsync()
            ) ?? throw new Exception("Failed to deserialize response content");
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.Status);
        Assert.Equal(expectedProblemType, problem.Type);
    }

    private HttpClient GetTestClient(
        IInstanceRepository? instanceRepository = null,
        IInstanceMutationRepository? instanceMutationRepository = null,
        bool enableWolverine = false,
        IProcessDataCleanupService? processDataCleanupService = null,
        IDataService? dataService = null,
        IApplicationService? applicationService = null,
        IProcessAuthorizer? processAuthorizer = null
    )
    {
        // No setup required for these services. They are not in use by the ApplicationController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
        Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();

        HttpClient client = _factory
            .WithWebHostBuilder(builder =>
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

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<IInstanceEventRepository, InstanceEventRepositoryMock>();
                    services.Configure<WolverineSettings>(opts =>
                    {
                        opts.EnableSending = enableWolverine;
                    });

                    if (instanceRepository != null)
                    {
                        services.AddSingleton(instanceRepository);
                    }
                    else
                    {
                        services.AddSingleton<IInstanceRepository, InstanceRepositoryMock>();
                    }

                    if (instanceMutationRepository != null)
                    {
                        services.AddSingleton(instanceMutationRepository);
                    }
                    else
                    {
                        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
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
                                (
                                    Guid _,
                                    long _,
                                    InstanceMutationCommit mutation,
                                    CancellationToken _
                                ) =>
                                    new InstanceMutationApplyResult(
                                        false,
                                        [],
                                        mutation.InstanceUpdates
                                    )
                            );
                        services.AddSingleton(mutationRepositoryMock.Object);
                    }

                    if (processDataCleanupService != null)
                    {
                        services.AddSingleton(processDataCleanupService);
                    }

                    if (dataService != null)
                    {
                        services.AddSingleton(dataService);
                    }

                    if (applicationService != null)
                    {
                        services.AddSingleton(applicationService);
                    }

                    if (processAuthorizer != null)
                    {
                        services.AddSingleton(processAuthorizer);
                    }
                });
            })
            .CreateClient();

        return client;
    }
}
