using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
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
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Constructor.
/// </summary>
/// <param name="factory">The web application factory.</param>
public class InstancesControllerTests(TestApplicationFactory<InstancesController> factory)
    : IClassFixture<TestApplicationFactory<InstancesController>>
{
    private const string BasePath = "storage/api/v1/instances";

    private readonly TestApplicationFactory<InstancesController> _factory = factory;

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Get_UserHasTooLowAuthLv_ReturnsStatusForbidden()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20475edd-dc38-4ae0-bd64-1b20643f506c";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 0);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("", 1L)]
    [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
    [InlineData("altinn:instances.read", null)]
    [InlineData("something", 1L)]
    public async Task Get_One_Ok(string scope, long? invalidScopeRequests)
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "46133fb5-a9f2-45d4-90b1-f6d93ad40713";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 3, scopes: [scope]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseContent = await response.Content.ReadAsStringAsync();
        Instance instance = (Instance)JsonConvert.DeserializeObject(responseContent, typeof(Instance));
        Assert.Equal("1337", instance.InstanceOwner.PartyId);
        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    [Fact]
    public async Task Get_One_Twice_Ok()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "377efa97-80ee-4cc6-8d48-09de12cc273d";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 3);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        RequestTracker.Clear();
        HttpResponseMessage response = await client.GetAsync(requestUri);
        HttpResponseMessage response2 = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(1, RequestTracker.GetRequestCount("GetDecisionForRequest1337/377efa97-80ee-4cc6-8d48-09de12cc273d"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string responseContent = await response.Content.ReadAsStringAsync();
        Instance instance = (Instance)JsonConvert.DeserializeObject(responseContent, typeof(Instance));
        Assert.Equal("1337", instance.InstanceOwner.PartyId);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    [Fact]
    public async Task Get_One_With_SyncAdapterScope_Ok()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "377efa97-80ee-4cc6-8d48-09de12cc273d";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        RequestTracker.Clear();
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(0, RequestTracker.GetRequestCount("GetDecisionForRequest1337/377efa97-80ee-4cc6-8d48-09de12cc273d")); // We should not be hitting the PDP as sync adapter
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string responseContent = await response.Content.ReadAsStringAsync();
        Instance instance = (Instance)JsonConvert.DeserializeObject(responseContent, typeof(Instance));
        Assert.Equal("1337", instance.InstanceOwner.PartyId);
    }

    /// <summary>
    /// Test case: User tries to access element that he is not authorized for
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Get_ReponseIsDeny_ReturnsStatusForbidden()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "23d6aa98-df3b-4982-8d8a-8fe67a53b828";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(1, 50001, 3);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: Response is deny.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Post_ReponseIsDeny_ReturnsStatusForbidden()
    {
        // Arrange
        string appId = "tdd/endring-av-navn";
        string requestUri = $"{BasePath}?appId={appId}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(-1, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Laste opp test instance..
        Instance instance = new Instance() { InstanceOwner = new InstanceOwner() { PartyId = "1337" }, Org = "tdd", AppId = "tdd/endring-av-navn" };

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: Sync adapters should not be allowed to create instances (only update data values and delete).
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Post_ReponseIsDenyForSyncAdapter_ReturnsStatusForbidden()
    {
        // Arrange
        string appId = "tdd/endring-av-navn";
        string requestUri = $"{BasePath}?appId={appId}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Laste opp test instance..
        Instance instance = new Instance() { InstanceOwner = new InstanceOwner() { PartyId = "1337" }, Org = "tdd", AppId = "tdd/endring-av-navn" };

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Post_UserHasTooLowAuthLv_ReturnsStatusForbidden()
    {
        // Arrange
        string appId = "tdd/endring-av-navn";
        string requestUri = $"{BasePath}?appId={appId}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 0);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Laste opp test instance..
        Instance instance = new Instance() { InstanceOwner = new InstanceOwner() { PartyId = "1337" }, Org = "tdd", AppId = "tdd/endring-av-navn" };

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Theory]
    [InlineData("", 1L)]
    [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
    [InlineData("altinn:instances.write", null)]
    [InlineData("something", 1L)]
    public async Task Post_Ok(string scope, long? invalidScopeRequests)
    {
        // Arrange
        string appId = "tdd/endring-av-navn";
        string requestUri = $"{BasePath}?appId={appId}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 3, scopes: [scope]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Instance instance = new Instance { InstanceOwner = new InstanceOwner { PartyId = "1337" } };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType.ToString());
        string json = await response.Content.ReadAsStringAsync();
        Instance createdInstance = JsonConvert.DeserializeObject<Instance>(json);
        Assert.NotNull(createdInstance);
        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    [Theory]
    [InlineData("", 1L)]
    [InlineData("altinn:serviceowner/instances.write", null)]
    public async Task Post_Org_Ok(string scope, long? invalidScopeRequests)
    {
        // Arrange
        string appId = "tdd/endring-av-navn";
        string requestUri = $"{BasePath}?appId={appId}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("tdd", scope: scope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Instance instance = new Instance { InstanceOwner = new InstanceOwner { PartyId = "1337" } };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType.ToString());
        string json = await response.Content.ReadAsStringAsync();
        Instance createdInstance = JsonConvert.DeserializeObject<Instance>(json);
        Assert.NotNull(createdInstance);
        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    /// <summary>
    /// Test case: User has to low authentication level.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Delete_UserHasTooLowAuthLv_ReturnsStatusForbidden()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337, 0);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: User tries to delete a element it is not authorized to delete
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Delete_ResponseIsDeny_ReturnsStatusForbidden()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(1, 1337, 3);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: App owner tries to hard delete an instance
    /// Expected: Returns success and deleted instance
    /// </summary>
    [Fact]
    public async Task Delete_OrgHardDeletesInstance_ReturnedInstanceHasStatusBothSoftAndHardDeleted()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}?hard=true";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("tdd");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        string json = await response.Content.ReadAsStringAsync();
        Instance deletedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.NotNull(deletedInstance.Status.HardDeleted);
        Assert.NotNull(deletedInstance.Status.SoftDeleted);
        Assert.Equal(deletedInstance.Status.HardDeleted, deletedInstance.Status.SoftDeleted);
    }

    [Fact]
    public async Task Delete_With_SyncAdapterScope_Ok()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "377efa97-80ee-4cc6-8d48-09de12cc273d";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}?hard=true";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        RequestTracker.Clear();
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        string json = await response.Content.ReadAsStringAsync();
        Instance deletedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.Equal(0, RequestTracker.GetRequestCount("GetDecisionForRequest1337/377efa97-80ee-4cc6-8d48-09de12cc273d")); // We should not be hitting the PDP as sync adapter
        Assert.NotNull(deletedInstance.Status.HardDeleted);
        Assert.NotNull(deletedInstance.Status.SoftDeleted);
        Assert.Equal(deletedInstance.Status.HardDeleted, deletedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test case: End user system tries to soft delete an instance
    /// Expected: Returns success and deleted instance
    /// </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstance_ReturnedInstanceHasStatusOnlySoftDeleted()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        string json = await response.Content.ReadAsStringAsync();
        Instance deletedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.Null(deletedInstance.Status.HardDeleted);
        Assert.NotNull(deletedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test case: End user system tries to soft delete an instance, but GetApplicationOrErrorAsync throws an exception
    /// Expected: Returns status internal server error.
    /// </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstance_GetApplicationOrErrorAsyncThrowsException_ReturnsStatusInternalServerError()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        Mock<IApplicationService> applicationService = new();
        applicationService.Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>())).ReturnsAsync((null, new ServiceError(500, "Something went wrong")));

        HttpClient client = GetTestClient(applicationService: applicationService);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Test case: End user system tries to soft delete an instance, but GetApplicationOrErrorAsync returns app info error
    /// Expected: Returns status Not Found.
    /// </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstance_GetApplicationOrErrorAsyncReturnsErrorNotFound_ReturnsStatusNotFound()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";

        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        Mock<IApplicationService> applicationService = new();
        applicationService.Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>())).ReturnsAsync((null, new ServiceError(404, "Not found")));

        HttpClient client = GetTestClient(applicationService: applicationService);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Test case: End user system tries to soft/hard delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays
    /// Expected: Returns status forbidden.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_EndUserSoftDeletesInstancePreventedFromDeletion_ReturnsStatusForbidden(bool hard)
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "3f7fcd91-114e-4da1-95b6-72115f34945c";
        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        if (hard)
        {
            requestUri += "?hard=true";
        }

        var archived = DateTime.Parse("2024-04-29T13:53:06.117891Z");
        int daysSinceArchived = (int)(DateTime.UtcNow - archived).TotalDays;

        Application application = new() { PreventInstanceDeletionForDays = daysSinceArchived + 10 }; // Prevent deletion for longer than the instance has been archived
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock.Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>())).ReturnsAsync((application, null));

        HttpClient client = GetTestClient(applicationService: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Instance cannot be deleted yet due to application restrictions.", responseMessage);
    }

    /// <summary>
    /// Test case: End user system tries to soft delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays, but the instance is not archived
    /// Expected: Returns success and deleted instance
    /// </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstancePreventedFromDeletion_InstanceNotArchived_ReturnsSuccess()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";
        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        Application application = new() { PreventInstanceDeletionForDays = 30 };
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock.Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>())).ReturnsAsync((application, null));

        Instance instance = new()
        {
            Status = new InstanceStatus(),
            AppId = "org123/app456",
            Id = "org123/app456",
            InstanceOwner = new InstanceOwner { PartyId = "1337" }
        };

        HttpClient client = GetTestClient(applicationService: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        Instance deletedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.NotNull(deletedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test case: End user system tries to hard delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays, but the instance is not archived
    /// Expected: Returns success and deleted instance
    /// </summary>
    [Fact]
    public async Task Delete_EndUserHardDeletesInstancePreventedFromDeletion_InstanceNotArchived_ReturnsSuccess()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";
        string requestUri = $"{BasePath}/{instanceOwnerId}/{instanceGuid}";

        requestUri += "?hard=true";

        Application application = new() { PreventInstanceDeletionForDays = 30 };
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock.Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>())).ReturnsAsync((application, null));

        Instance instance = new()
        {
            Status = new InstanceStatus(),
            AppId = "org123/app456",
            Id = "org123/app456",
            InstanceOwner = new InstanceOwner { PartyId = "1337" }
        };

        HttpClient client = GetTestClient(applicationService: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        Instance deletedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.NotNull(deletedInstance.Status.HardDeleted);
        Assert.NotNull(deletedInstance.Status.SoftDeleted);
        Assert.Equal(deletedInstance.Status.HardDeleted, deletedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test case: Org user requests to get multiple instances from one of their apps.
    /// Expected: List of instances is returned.
    /// </summary>
    [Fact]
    public async Task GetMany_OrgRequestsAllAppInstances_Ok()
    {
        // Arrange
        string requestUri = $"{BasePath}?appId=ttd/complete-test";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 4;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Test case: Org user requests to get multiple instances from one of their apps.
    /// Expected: List of instances is returned.
    /// </summary>
    [Fact]
    public async Task GetMany_OrgRequestsAllAppInstancesAlternativeScope_Ok()
    {
        // Arrange
        string requestUri = $"{BasePath}?appId=ttd/complete-test";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 4;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Test case: Org user requests to get all instances linked to their org.
    /// Expected: List of instances is returned.
    /// </summary>
    [Theory]
    [InlineData("", 1L)]
    [InlineData("altinn:serviceowner/instances.read", null)]
    public async Task GetMany_OrgRequestsAllInstances_Ok(string scope, long? invalidScopeRequests)
    {
        // Arrange
        string requestUri = $"{BasePath}?org=ttd";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("ttd", scope: scope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 14;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        if (scope != string.Empty)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedNoInstances, queryResponse.Count);
        }

        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    /// <summary>
    /// Test case: User requests to get multiple instances from a single instanceOwner - themselves.
    /// Expected: List of instances is returned.
    /// </summary>
    [Fact]
    public async Task GetMany_PartyRequestsOwnInstances_Ok()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.partyId=1600";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(10016, 1600, 4);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 9;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Test case: A system user requests all instances from a specific instance owner that they have access to.
    /// Expected: List of instances is returned.
    /// </summary>
    [Fact]
    public async Task GetMany_SystemUserRequestsInstances_SystemUserHasAccess_Ok()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.partyId=1337";

        HttpClient client = GetTestClient();
        string systemUserId = "49913a53-0bf9-47fc-9cad-6aa3dc825008";
        string token = PrincipalUtil.GetSystemUserToken(systemUserId, "725736800");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 38;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Test case: User requests to get multiple instances from a single instanceOwner they represent.
    /// Expected: List of instances is returned after unathorized instances are removed.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsAnotherPartiesInstances_Ok()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.partyId=1600";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int expectedNoInstances = 3;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Test case: Get Multiple instances without specifying instance owner partyId.
    /// Expected: Returns status bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdDefined_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string expected = "Either InstanceOwnerPartyId or InstanceOwnerIdentifier need to be defined.";

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances without specifying instance owner partyId.
    /// Expected: Returns status bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdButWithWrongInstanceOwnerIdDefined_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "something:3312321321");
        string expected = "Invalid InstanceOwnerIdentifier.";

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances with person number and without specifying instance owner partyId.
    /// Expected: Returns internal server error.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdDefinedAndWithPerson_ReturnsOK()
    {
        // Arrange
        string requestUri = $"{BasePath}";
        int partyId = 1337;

        Mock<IRegisterService> registerService = new Mock<IRegisterService>();
        registerService.Setup(x => x.PartyLookup(It.Is<string>(p => p == "33312321321"), It.Is<string>(o => o == null))).ReturnsAsync(partyId);

        HttpClient client = GetTestClient(null, registerService);
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Person:33312321321");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        registerService.VerifyAll();
    }

    /// <summary>
    /// Test case: Get Multiple instances with person number and instance owner partyId.
    /// Expected: Returns status bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesWithPartyIdDefinedAndWithPerson_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.partyId=1600";
        int partyId = 1337;
        string expected = "Both InstanceOwner.PartyId and InstanceOwnerIdentifier cannot be present at the same time.";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Person:33312321321");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances with organisation number and without specifying instance owner partyId.
    /// Expected: Returns internal server error.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdDefinedAndWithOrganisation_ReturnsOK()
    {
        // Arrange
        string requestUri = $"{BasePath}";
        int partyId = 1337;

        Mock<IRegisterService> registerService = new Mock<IRegisterService>();
        registerService.Setup(x => x.PartyLookup(It.Is<string>(p => p == null), It.Is<string>(o => o == "333123213"))).ReturnsAsync(partyId);

        HttpClient client = GetTestClient(null, registerService);
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Organisation:333123213");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        registerService.VerifyAll();
    }

    /// <summary>
    /// Test case: Get empty list of instances with wrong organisation number and without specifying instance owner partyId.
    /// Expected: Returns empty list of instances with HTTP status OK.
    /// </summary>
    [Fact]
    public async Task GetEmptyListOfInstances_UserRequestsInstancesNoPartyIdDefinedAndWithOrganisation_ReturnsOK()
    {
        // Arrange
        string requestUri = $"{BasePath}";
        int partyId = -1;

        Mock<IRegisterService> registerService = new Mock<IRegisterService>();
        registerService.Setup(x => x.PartyLookup(It.Is<string>(p => p == null), It.Is<string>(o => o == "333123213"))).ReturnsAsync(partyId);

        HttpClient client = GetTestClient(null, registerService);
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Organisation:333123213");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        registerService.VerifyAll();
    }

    /// <summary>
    /// Test case: Get Multiple instances with invalid organisation number and without specifying instance owner partyId.
    /// Expected: Controller returns 400 bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdDefinedAndWithInvalidOrganisation_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}";
        int partyId = 1337;
        string expectedResponseMessage = "Organization number needs to be exactly 9 digits.";

        HttpClient client = GetTestClient(null, null);
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Organisation:33312");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedResponseMessage, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances with invalid organisation number and without specifying instance owner partyId.
    /// Expected: Controller returns 400 bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstancesNoPartyIdDefinedAndWithInvalidPerson_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}";
        int partyId = 1337;
        string expectedResponseMessage = "Person number needs to be exactly 11 digits.";

        HttpClient client = GetTestClient(null, null);
        string token = PrincipalUtil.GetToken(3, partyId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Ai-InstanceOwnerIdentifier", "Person:33312");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedResponseMessage, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances and specifying status.isHardDeleted=true.
    /// Expected: No instances included in response.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsHardDeletedInstances_EmptyListReturned()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.partyId=1337&status.IsHardDeleted=true";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(responseMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, queryResponse.Count);
    }

    /// <summary>
    /// Test case: Get Multiple instances for a partyId.
    /// Expected: status.isHardDeleted=false is included in query parameters sent to repository.
    /// </summary>
    [Fact]
    public async Task GetMany_UserRequestsInstances_HardDeletedFalseQueryParamIncluded()
    {
        // Arrange
        Mock<IInstanceRepository> irm = new();
        irm
        .Setup(irm =>
        irm.GetInstancesFromQuery(
            It.Is<InstanceQueryParameters>(e => e.IsHardDeleted == false),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new InstanceQueryResponse { Instances = new() });

        string requestUri = $"{BasePath}?instanceOwner.partyId=1337";

        HttpClient client = GetTestClient(irm);
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();
        JsonConvert.DeserializeObject<InstanceQueryResponse>(responseMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        irm.VerifyAll();
    }

    [Fact]
    public async Task GetMany_ContainsContinuationToken_CorrectContTokenInSelfLink()
    {
        // Arrange
        Mock<IInstanceRepository> irm = new();
        irm
        .Setup(irm =>
        irm.GetInstancesFromQuery(
            It.Is<InstanceQueryParameters>(e => e.IsHardDeleted == false),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new InstanceQueryResponse { Instances = new() });

        string requestUri = $"{BasePath}?instanceOwner.partyId=1337&continuationToken=thisIsTheFirstToken";

        HttpClient client = GetTestClient(irm);
        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(responseMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("continuationToken=thisIsTheFirstToken", queryResponse.Self);
    }

    /// <summary>
    /// Test case: Get Multiple instances without specifying org.
    /// Expected: Returns status bad request.
    /// </summary>
    [Fact]
    public async Task GetMany_OrgRequestsInstancesNoOrgDefined_ReturnsBadRequest()
    {
        // Arrange
        string requestUri = $"{BasePath}";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("testOrg", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string expected = "Org or AppId must be defined.";

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, responseMessage);
    }

    /// <summary>
    /// Test case: Get Multiple instances using client with incorrect scope.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task GetMany_IncorrectScope_ReturnsForbidden()
    {
        // Arrange
        string requestUri = $"{BasePath}?org=testOrg";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("testOrg", scope: "altinn:serviceowner/instances.write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Test case: Get Multiple instances using client with sync adapter scope should work.
    /// Expected: Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task GetMany_SyncAdapterScope_OK()
    {
        // Arrange
        string requestUri = $"{BasePath}?org=ttd";

        var expectedNoInstances = 14;

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("testOrg", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);
        string json = await response.Content.ReadAsStringAsync();
        InstanceQueryResponse queryResponse = JsonConvert.DeserializeObject<InstanceQueryResponse>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedNoInstances, queryResponse.Count);
    }

    /// <summary>
    /// Scenario:
    ///   The sync adapter calls the API via apps-endpoints authenticated digdir, which is not the app's service owner.
    ///   This should be allowed regardless of policy.
    /// Result:
    ///   Returns status is OK.
    /// </summary>
    [Fact]
    public async Task GetMany_QueryingDifferentOrgThanInClaims_ReturnsOKIfSyncAdapter()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.PartyId=1337&appId=sfvt/test-read-app-no-permission";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("digdir", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   An app owner calls the API via apps-endpoints with a token that specifies a different
    ///   organization than the service owner. As long as the policy file does not explicitly
    ///   allow the different organization to access instances, the request should be denied.
    /// Result:
    ///   Returns status is Forbidden.
    /// </summary>
    [Fact]
    public async Task GetMany_QueryingDifferentOrgThanInClaims_ReturnsForbiddenIfDifferentOrgIsNotAuthenticatedWithPolicyFile()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.PartyId=1337&appId=sfvt/test-read-app-no-permission";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("digdir", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   An app owner calls the API via apps-endpoints with a token that specifies the same organization
    ///   as the service owner. The policy file does not explicitly allow the different organization to access
    ///   instances, the request should still be allowed. 
    /// Result:
    ///   Returns status is OK.
    /// </summary>
    [Fact]
    public async Task GetMany_QueryingSameOrgAsInClaims_ReturnsOk()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.PartyId=1337&appId=sfvt/app-without-policy";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("sfvt", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   An app owner calls the API via apps-endpoints with a token that specifies a different
    ///   organization than the service owner. As long as the policy file specifies that the different
    ///   organization should be allowed to access instances, the request should be allowed.
    /// Result:
    ///   Returns status OK.
    /// </summary>
    [Fact]
    public async Task GetMany_QueryingDifferentOrgThanInClaims_ReturnsOkIfDifferentOrgIsAuthenticatedWithPolicyFile()
    {
        // Arrange
        string requestUri = $"{BasePath}?instanceOwner.PartyId=1337&appId=sfvt/test-read-app";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetOrgToken("digdir", scope: "altinn:serviceowner/instances.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   A stakeholder calls the complete operation to indicate that they consider the instance as completed.
    ///   The stakeholder is authorized and it is the first times they make this call.
    /// Result:
    ///   The given instance is updated with a new entry in CompleteConfirmations.
    /// </summary>
    [Fact]
    public async Task AddCompleteConfirmation_PostAsValidAppOwner_RespondsWithUpdatedInstance()
    {
        // Arrange
        string org = "tdd";
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "2f7fa5ce-e878-4e1f-a241-8c0eb1a83eab";
        string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/complete";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken(org);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(string.Empty));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Don't compare original and updated instance in asserts. The two instances are identical.
        Assert.NotNull(updatedInstance);
        Assert.Equal(org, updatedInstance.CompleteConfirmations[0].StakeholderId);
        Assert.Equal("111111111", updatedInstance.LastChangedBy);
        Assert.Equal(instanceId, updatedInstance.Id);
    }

    /// <summary>
    /// Scenario:
    ///   A stakeholder calls the complete operation to indicate that they consider the instance as completed.
    ///   Something goes wrong when trying to save the updated instancee.
    /// Result:
    ///   The operation returns status InternalServerError
    /// </summary>
    [Fact]
    public async Task AddCompleteConfirmation_ExceptionDuringInstanceUpdate_ReturnsInternalServerError()
    {
        // Arrange
        string org = "tdd";
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "d3b326de-2dd8-49a1-834a-b1d23b11e540";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/complete";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken(org);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(string.Empty));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   A stakeholder calls the complete operation to indicate that they consider the instance as completed, but
    ///   they have already done so from before. The API makes no changes and return the original instancee.
    /// Result:
    ///   The given instance keeps the existing complete confirmation.
    /// </summary>
    [Fact]
    public async Task AddCompleteConfirmation_PostAsValidAppOwnerTwice_RespondsWithSameInstance()
    {
        // Arrange
        string org = "tdd";
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "ef1b16fc-4566-4577-b2d8-db74fbee4f7c";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/complete";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken(org);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(string.Empty));

        if (response.StatusCode.Equals(HttpStatusCode.InternalServerError))
        {
            string serverContent = await response.Content.ReadAsStringAsync();
            throw new Exception(serverContent);
        }

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Don't compare original and updated instance. The two variables point to the same instance.
        Assert.NotNull(updatedInstance);
        Assert.Equal(org, updatedInstance.CompleteConfirmations[0].StakeholderId);
        Assert.Equal("1337", updatedInstance.LastChangedBy);

        // Verify it is the stored instance that is returned
        Assert.Equal(6, updatedInstance.CompleteConfirmations[0].ConfirmedOn.Minute);
    }

    /// <summary>
    /// Scenario:
    ///   A stakeholder calls the complete operation to indicate that they consider the instance as completed, but
    ///   the attempt to get the instance from the document database fails in an exception.
    /// Result:
    ///   The response has status code 500.
    /// </summary>
    [Fact]
    public async Task AddCompleteConfirmation_CompleteNonExistentInstance_ExceptionDuringAuthorization_RespondsWithInternalServerError()
    {
        // Arrange
        string org = "tdd";
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "406d1e74-e4f5-4df1-833f-06ef16246a6f";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/complete";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken(org);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(string.Empty));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   A stakeholder calls the complete operation to indicate that they consider the instance as completed, but
    ///   the attempt to get the instance from the document database fails in an exception.
    /// Result:
    ///   The response has status code 500.
    /// </summary>
    [Fact]
    public async Task AddCompleteConfirmation_AttemptToCompleteInstanceAsUser_ReturnsForbidden()
    {
        // Arrange
        string org = "brg";
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "8727385b-e7cb-4bf2-b042-89558c612826";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/complete";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken(org);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(string.Empty));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    /// Update read status for an instance where the status has not been initialized yet.
    /// Result:
    /// Read status is successfuly updated and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateReadStatus_SetInitialReadStatus_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "824e8304-ad9e-4d79-ac75-bcfa7213223b";

        ReadStatus expectedReadStus = ReadStatus.Read;

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/readstatus?status=read";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri);

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedReadStus, updatedInstance.Status.ReadStatus);
    }

    /// <summary>
    /// Scenario:
    /// Update read status for an instance with current status 'read'.
    /// Result:
    /// Read status is successfuly updated and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateReadStatus_FromReadToUnread_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "d9a586ca-17ab-453d-9fc5-35eaadb3369b";
        ReadStatus expectedReadStus = ReadStatus.Unread;

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/readstatus?status=unread";
        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri);

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedReadStus, updatedInstance.Status.ReadStatus);
    }

    /// <summary>
    /// Scenario:
    /// Trying to update an instance with an invalid read status.
    /// Result:
    /// Response code is bad request.
    /// </summary>
    [Fact]
    public async Task UpdateReadStatus_InvalidStatus_ReturnsBadRequest()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "d9a586ca-17ab-453d-9fc5-35eaadb3369b";
        string expectedMessage = $"Invalid read status: invalid. Accepted types include: {string.Join(", ", Enum.GetNames(typeof(ReadStatus)))}";

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/readstatus?status=invalid";
        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri);

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);
        string json = await response.Content.ReadAsStringAsync();
        string actualMessage = JsonConvert.DeserializeObject<string>(json);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedMessage, actualMessage);
    }

    /// <summary>
    /// Scenario:
    /// Update substatus for an instance where the substatus has not been initialized yet.
    /// Result:
    /// substatus is successfuly updated and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateSubstatus_SetInitialSubstatus_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20475edd-dc38-4ae0-bd64-1b20643f506c";

        Substatus expectedSubstatus = new Substatus { Label = "Substatus.Approved.Label", Description = "Substatus.Approved.Description" };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/substatus";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken("tdd");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(expectedSubstatus, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.NotNull(updatedInstance);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSubstatus.Label, updatedInstance.Status.Substatus.Label);
        Assert.Equal(expectedSubstatus.Description, updatedInstance.Status.Substatus.Description);
        Assert.Equal("111111111", updatedInstance.LastChangedBy);
        Assert.True(updatedInstance.LastChanged > DateTime.UtcNow.AddMinutes(-5));
    }

    /// <summary>
    /// Scenario:
    /// Update substatus for an instance where there is a pre-existing substatus.
    /// Result:
    /// substatus is completely overwritten by the new substatus.
    /// </summary>
    [Fact]
    public async Task UpdateSubstatus_OverwriteSubstatus_DescriptionIsEmpty()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "67f568ce-f114-48e7-ba12-dd422f73667a";

        Substatus expectedSubstatus = new Substatus { Label = "Substatus.Approved.Label" };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/substatus";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken("tdd");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(expectedSubstatus, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);

        // Assert
        Assert.NotNull(updatedInstance);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSubstatus.Label, updatedInstance.Status.Substatus.Label);
        Assert.Equal(expectedSubstatus.Description, updatedInstance.Status.Substatus.Description);
        Assert.Equal("111111111", updatedInstance.LastChangedBy);
        Assert.True(updatedInstance.LastChanged > DateTime.UtcNow.AddMinutes(-5));
    }

    /// <summary>
    /// Scenario:
    /// Actor with user claims attemts to update substatus for an instance.
    /// Result:
    /// Response is 403 forbidden.
    /// </summary>
    [Fact]
    public async Task UpdateSubstatus_EndUserTriestoSetSubstatus_ReturnsForbidden()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "824e8304-ad9e-4d79-ac75-bcfa7213223b";

        Substatus substatus = new Substatus { Label = "Substatus.Approved.Label", Description = "Substatus.Approved.Description" };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/substatus";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(substatus, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    /// Org tries to update substatus without setting label.
    /// Result:
    /// Response is 400 bas request.
    /// </summary>
    [Fact]
    public async Task UpdateSubstatus_MissingLabel_ReturnsBadRequest()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "824e8304-ad9e-4d79-ac75-bcfa7213223b";

        Substatus substatus = new Substatus { Description = "Substatus.Approved.Description" };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/substatus";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken("tdd");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(substatus, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    /// Add presentation fields to an instance that doesn't have any existing presentation fields
    /// Result:
    /// Presentation fields are succesfully added and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdatePresentationFields_NoPreviousFieldsSet_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";

        PresentationTexts presentationTexts = new PresentationTexts
        {
            Texts = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            }
        };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/presentationtexts";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(presentationTexts, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.PresentationTexts;

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(2, actual.Keys.Count);
    }

    /// <summary>
    /// Scenario:
    /// Update an existing presentation field 
    /// Result:
    /// Presentation field are succesfully updated, other fields are untouched and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdatePresentationFields_UpdateAnExistingPresentationField_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";

        PresentationTexts presentationTexts = new PresentationTexts
        {
            Texts = new Dictionary<string, string>
            {
                { "key1", "updatedvalue1" },
            }
        };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/presentationtexts";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(presentationTexts, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.PresentationTexts;

        // Assert
        Assert.Equal(2, actual.Keys.Count);
        Assert.True(actual.ContainsKey("key2"));
        Assert.Equal("updatedvalue1", actual["key1"]);
    }

    /// <summary>
    /// Scenario:
    /// Delete an existing presentation field 
    /// Result:
    /// Presentation field is succesfully removed, other fields are untouched and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdatePresentationFields_RemoveAnExistingPresentationField_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";

        const string removedKey = "key1";

        PresentationTexts presentationTexts = new PresentationTexts
        {
            Texts = new Dictionary<string, string>
            {
                { removedKey, string.Empty },
            }
        };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/presentationtexts";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(presentationTexts, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.PresentationTexts;

        // Assert
        Assert.Single(actual.Keys);
        Assert.True(actual.ContainsKey("key2"));
        Assert.False(actual.ContainsKey(removedKey));
    }

    /// <summary>
    /// Scenario:
    /// Add a new presentation field to an already existing collection of presentation fields
    /// Result:
    /// Presentation field is succesfully added to existing collection and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdatePresentationFields_AddNewPresentationFieldToExistingCollection_ReturnsUpdatedInstance()
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";

        PresentationTexts presentationTexts = new PresentationTexts
        {
            Texts = new Dictionary<string, string>
            {
                { "key3", "value3" },
            }
        };

        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/presentationtexts";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(presentationTexts, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.PresentationTexts;

        // Assert
        Assert.Equal(3, actual.Keys.Count);
    }

    /// <summary>
    /// Scenario:
    /// Passes in null as presentation texts.
    /// Result:
    /// The existing collection is left as is, and a 400 Bad request is returned
    /// </summary>
    [Theory]
    [MemberData(nameof(GetPresentationTextsData))]
    public async Task UpdatePresentationFields_PassingNullAsPresentationTexts_Returns400(PresentationTexts presentationTexts)
    {
        // Arrange            
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestPutUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/presentationtexts";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpPutRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestPutUri)
        {
            Content = JsonContent.Create(presentationTexts, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpPutRequestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object[]> GetPresentationTextsData()
    {
        yield return new object[] { new PresentationTexts() { Texts = null } };
        yield return new object[] { null };
    }

    /// <summary>
    /// Scenario:
    /// Add the value of a data field to an instance that doesn't have any existing data values
    /// Result:
    /// Data values are succesfully added and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateDataValues_NoPreviousValuesSet_ReturnsUpdatedInstance()
    {
        // Arrange
        var dataValues = new DataValues
        {
            Values = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            }
        };

        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.DataValues;

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(2, actual.Keys.Count);
    }

    /// <summary>
    /// Scenario:
    /// Update an existing data value 
    /// Result:
    /// Data values are succesfully updated, other values are untouched and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateDataValues_UpdateAnExistingDataValue_ReturnsUpdatedInstance()
    {
        // Arrange
        var dataValues = new DataValues
        {
            Values = new Dictionary<string, string>
            {
                { "key1", "updatedvalue1" },
            }
        };

        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.DataValues;

        // Assert
        Assert.Equal(2, actual.Keys.Count);
        Assert.True(actual.ContainsKey("key2"));
        Assert.Equal("updatedvalue1", actual["key1"]);
    }

    /// <summary>
    /// Scenario:
    /// Delete an existing data value 
    /// Result:
    /// Data value is succesfully removed, other fields are untouched and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateDataValues_RemoveAnExistingDataValue_ReturnsUpdatedInstance()
    {
        // Arrange
        const string removedKey = "key1";

        var dataValues = new DataValues
        {
            Values = new Dictionary<string, string>
            {
                { removedKey, string.Empty },
            }
        };

        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.DataValues;

        // Assert
        Assert.Single(actual.Keys);
        Assert.True(actual.ContainsKey("key2"));
        Assert.False(actual.ContainsKey(removedKey));
    }

    /// <summary>
    /// Scenario:
    /// Add a new data value to an already existing collection of data values
    /// Result:
    /// Data value is succesfully added to existing collection and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateDataValues_AddNewDataValueToExistingCollection_ReturnsUpdatedInstance()
    {
        // Arrange            
        var dataValues = new DataValues
        {
            Values = new Dictionary<string, string>
            {
                { "key3", "value3" },
            }
        };

        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.DataValues;

        // Assert
        Assert.Equal(3, actual.Keys.Count);
        Assert.Equal("value3", actual["key3"]);
    }

    /// <summary>
    /// Scenario:
    /// Passes in null as datavalue.
    /// Result:
    /// The existing collection is left as is, and a 400 Bad request is returned
    /// </summary>
    [Theory]
    [MemberData(nameof(GetDataValuesData))]
    public async Task UpdateDataValues_PassingNullAsDataValues_Returns400(DataValues dataValues)
    {
        // Arrange
        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestPutUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetToken(3, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpPutRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestPutUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpPutRequestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    /// Add the value of a data field to an instance using the sync adapter scope should succeed
    /// Result:
    /// Data values are succesfully added and the updated instance returned.
    /// </summary>
    [Fact]
    public async Task UpdateDataValues_NoPreviousValuesSet_WithSyncAdapterScope_ReturnsUpdatedInstance()
    {
        // Arrange
        var dataValues = new DataValues
        {
            Values = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            }
        };

        int instanceOwnerPartyId = 1337;
        string instanceGuid = "20a1353e-91cf-44d6-8ff7-f68993638ffe";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/datavalues";

        HttpClient client = GetTestClient();

        string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:storage/instances.syncadapter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(dataValues, new MediaTypeHeaderValue("application/json"))
        };

        // Act
        HttpResponseMessage response = await client.SendAsync(httpRequestMessage);

        string json = await response.Content.ReadAsStringAsync();
        Instance updatedInstance = JsonConvert.DeserializeObject<Instance>(json);
        Dictionary<string, string> actual = updatedInstance.DataValues;

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(2, actual.Keys.Count);
    }

    public static IEnumerable<object[]> GetDataValuesData()
    {
        yield return new object[] { new DataValues() { Values = null } };
        yield return new object[] { null };
    }

    private TestTelemetry _testTelemetry;

    private HttpClient GetTestClient(Mock<IInstanceRepository> repositoryMock = null, Mock<IRegisterService> registerService = null, Mock<IApplicationService> applicationService = null)
    {
        // No setup required for these services. They are not in use by the InstanceController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
        Mock<IMessageBus> busMock = new Mock<IMessageBus>();
        
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
            builder.ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddConfiguration(configuration);
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();

                if (repositoryMock != null)
                {
                    services.AddSingleton(repositoryMock.Object);
                }

                if (registerService != null)
                {
                    services.AddSingleton(registerService.Object);
                }

                if (applicationService != null)
                {
                    services.AddSingleton(applicationService.Object);
                }

                services.AddSingleton(keyVaultWrapper.Object);

                services.AddSingleton<IPartiesWithInstancesClient, PartiesWithInstancesClientMock>();
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                services.AddSingleton(busMock.Object);
            });
        });

        var client = factory.CreateClient();

        _testTelemetry = factory.Services.GetRequiredService<TestTelemetry>();

        return client;
    }
}
