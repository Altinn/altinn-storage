#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
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
        IInstanceAndEventsRepository? instanceAndEventsRepository = null,
        Action<ProcessState>? configure = null
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
            jsonString = JsonContent.Create(update, new MediaTypeHeaderValue("application/json"));
        }
        else
        {
            ProcessState state = new();
            configure?.Invoke(state);
            jsonString = JsonContent.Create(state, new MediaTypeHeaderValue("application/json"));
        }

        HttpClient client = GetTestClient(instanceRepository, instanceAndEventsRepository);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        return await client.PutAsync(requestUri, jsonString);
    }

    public static TheoryData<bool> UpdateTestParameters => new() { { true }, { false } };

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
            .ReturnsAsync((testInstance, 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(testInstance, It.IsAny<List<string>>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(testInstance);

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
            .ReturnsAsync((testInstance, 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(testInstance, It.IsAny<List<string>>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(testInstance);

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
            .ReturnsAsync((testInstance, 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(testInstance, It.IsAny<List<string>>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(testInstance);

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
            .ReturnsAsync((testInstance, 0));
        instanceRepoMock
            .Setup(ir =>
                ir.Update(testInstance, It.IsAny<List<string>>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(testInstance);

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
        Mock<IInstanceAndEventsRepository> batchRepositoryMock =
            new Mock<IInstanceAndEventsRepository>();
        repositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((testInstance, 0));
        repositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<Instance>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((Instance i, List<string> _, CancellationToken _) => i);
        batchRepositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<Instance>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<List<InstanceEvent>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Instance i, List<string> _, List<InstanceEvent> _, CancellationToken _) => i
            );

        // Act
        using HttpResponseMessage response = await SendUpdateRequest(
            useInstanceAndEventsEndpoint,
            token: token,
            instanceId: "1337/377efa97-80ee-4cc6-8d48-09de12cc273d",
            instanceRepository: repositoryMock.Object,
            instanceAndEventsRepository: batchRepositoryMock.Object,
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
        string[] result = ProcessController.GetActionsThatAllowProcessNextForTaskType(taskType);

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

    private HttpClient GetTestClient(
        IInstanceRepository? instanceRepository = null,
        IInstanceAndEventsRepository? instanceAndEventsRepository = null,
        bool enableWolverine = false
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

                    if (instanceAndEventsRepository != null)
                    {
                        services.AddSingleton(instanceAndEventsRepository);
                    }
                    else
                    {
                        services.AddSingleton<
                            IInstanceAndEventsRepository,
                            InstanceAndEventsRepositoryMock
                        >();
                    }
                });
            })
            .CreateClient();

        return client;
    }
}
