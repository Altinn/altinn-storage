#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
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
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Initializes a new instance of the <see cref="MessageBoxInstancesControllerTests"/> class with the given <see cref="WebApplicationFactory{TStartup}"/>.
/// </summary>
/// <param name="factory">The <see cref="TestApplicationFactory{TStartup}"/> to use when setting up the test server.</param>
public class MessageBoxInstancesControllerTests(
    TestApplicationFactory<MessageBoxInstancesController> factory
) : IClassFixture<TestApplicationFactory<MessageBoxInstancesController>>
{
    private readonly TestApplicationFactory<MessageBoxInstancesController> _factory = factory;

    private const string BasePath = "/storage/api/v1";

    /// <summary>
    /// Scenario:
    ///   Request an existing instance.
    /// Expected:
    ///  A converted instance is returned.
    /// Success:
    ///  The instance has the expected properties.
    /// </summary>
    [Fact]
    public async Task GetMessageBoxInstance_RequestsExistingInstance_InstanceIsSuccessfullyMappedAndReturned()
    {
        // Arrange
        string instanceId = "1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a";
        string expectedTitle = "Name change, Sophie Salt";
        string expectedSubstatusLabel = "Application approved";

        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage responseMessage = await client.GetAsync(
            $"{BasePath}/sbl/instances/{instanceId}?language=en"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

        string responseContent = await responseMessage.Content.ReadAsStringAsync();
        const string expectedJson =
            "{\"id\":\"6323a337-26e7-4d40-89e8-f5bb3d80be3a\",\"instanceOwnerId\":\"1337\",\"org\":\"tdd\",\"appName\":\"endring-av-navn\",\"title\":\"Name change, Sophie Salt\",\"processCurrentTask\":\"Feedback\",\"createdDateTime\":\"2020-04-29T13:53:02.2836971Z\",\"lastChangedBy\":\"1337\",\"dueDateTime\":null,\"deleteStatus\":0,\"readStatus\":\"Unread\",\"substatus\":{\"label\":\"Application approved\",\"description\":\"Application approved by all instances.\"},\"allowDelete\":true,\"allowNewCopy\":false,\"authorizedForWrite\":true,\"authorizedForSign\":false,\"archivedDateTime\":null,\"deletedDateTime\":null,\"presentationText\":\"Sophie Salt\",\"dataValues\":null}";
        Assert.Equal(expectedJson, responseContent);
        MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(
            responseContent
        );

        Assert.Equal(expectedTitle, actual.Title);
        Assert.True(actual.AllowDelete);
        Assert.True(actual.AuthorizedForWrite);
        Assert.Equal(expectedSubstatusLabel, actual.Substatus.Label);
    }

    /// <summary>
    /// Scenario:
    ///   Request an existing instance.
    /// Expected:
    ///  A converted instance is returned.
    /// Success:
    ///  The instance does not have allowed to delete permissions.
    /// </summary>
    [Fact]
    public async Task GetMessageBoxInstance_RequestsExistingInstanceUserCannotDelete_InstanceIsSuccessfullyMappedAndReturned()
    {
        // Arrange
        string instanceId = "1606/6323a337-26e7-4d40-89e8-f5bb3d80be3b";

        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1606, 3)
        );

        // Act
        HttpResponseMessage responseMessage = await client.GetAsync(
            $"{BasePath}/sbl/instances/{instanceId}?language=en"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

        string responseContent = await responseMessage.Content.ReadAsStringAsync();
        MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(
            responseContent
        );

        Assert.False(actual.AllowDelete);
        Assert.True(actual.AuthorizedForWrite);
    }

    /// <summary>
    /// Scenario:
    ///   Request an instance the user is not authorized to see
    /// Expected:
    ///   Authorization stops the request
    /// Success:
    ///   Forbidden response.
    /// </summary>
    [Fact]
    public async Task GetMessageBoxInstance_RequestsInstanceUserIsNotAuthorized_ForbiddenReturned()
    {
        // Arrange
        string instanceId = "1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a";

        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(1, 1606, 3)
        );

        // Act
        HttpResponseMessage responseMessage = await client.GetAsync(
            $"{BasePath}/sbl/instances/{instanceId}?language=en"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, responseMessage.StatusCode);
    }

    [Fact]
    public async Task GetMessageBoxInstance_ArchivedInstanceCanBeCopied_UserHaveRights_InstanceIsSuccessfullyMappedAndReturned()
    {
        // Arrange
        string instanceId = "1337/07274f48-8313-4e2d-9788-bbdacef5a54e";

        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage responseMessage = await client.GetAsync(
            $"{BasePath}/sbl/instances/{instanceId}?language=en"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

        string responseContent = await responseMessage.Content.ReadAsStringAsync();
        MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(
            responseContent
        );

        Assert.True(actual.AllowNewCopy);
    }

    /// <summary>
    /// Scenario:
    ///   Soft delete an active instance in storage.
    /// Expected result:
    ///   Instance is marked for soft delete.
    /// Success criteria:
    ///   True is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_SoftDeleteActiveInstance_InstanceIsMarked_EventIsCreated_ReturnsTrue()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/08274f48-8313-4e2d-9788-bbdacef5a54e?hard=false"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        bool actualResult = JsonConvert.DeserializeObject<bool>(content);
        Assert.True(actualResult);
    }

    /// <summary>
    /// Scenario:
    ///   Soft delete an active instance in storage but user has too low authentication level.
    /// Expected result:
    ///   Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Delete_UserHasTooLowAuthLv_ReturnsStatusForbidden()
    {
        // Arrange
        Mock<IInstanceEventRepository> instanceEventRepository =
            new Mock<IInstanceEventRepository>();
        instanceEventRepository
            .Setup(s => s.InsertInstanceEvent(It.IsAny<InstanceEvent>(), null))
            .ReturnsAsync((InstanceEvent r, object o) => r);

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(1337, 1337, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a?hard=false"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(content));
    }

    /// <summary>
    /// Scenario:
    ///   Soft delete an active instance in storage but reponse is deny.
    /// Expected result:
    ///   Returns status forbidden.
    /// </summary>
    [Fact]
    public async Task Delete_ResponseIsDeny_ReturnsStatusForbidden()
    {
        // Arrange
        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(-1, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a?hard=false"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(content));
    }

    /// <summary>
    /// Scenario:
    ///   Hard delete a soft deleted instance in storage.
    /// Expected result:
    ///   Instance is marked for hard delete.
    /// Success criteria:
    ///   True is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_HardDeleteSoftDeleted()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/7a951b5b-ef96-4032-9273-f8d7651266f4?hard=true"
        );

        // Assert
        HttpStatusCode actualStatusCode = response.StatusCode;
        string content = await response.Content.ReadAsStringAsync();
        bool actualResult = JsonConvert.DeserializeObject<bool>(content);

        HttpStatusCode expectedStatusCode = HttpStatusCode.OK;
        bool expectedResult = true;
        Assert.Equal(expectedResult, actualResult);
        Assert.Equal(expectedStatusCode, actualStatusCode);
    }

    /// <summary>
    /// Scenario:
    ///  Delete an active instance, user has write priviliges
    /// Expected result:
    ///   Instance is marked for hard delete.
    /// Success criteria:
    ///   True is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_ActiveHasRole_ReturnsOk()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/d9a586ca-17ab-453d-9fc5-35eaadb3369b?hard=true"
        );
        string content = await response.Content.ReadAsStringAsync();
        bool actualResult = JsonConvert.DeserializeObject<bool>(content);

        // Assert
        Assert.True(actualResult);
    }

    /// <summary>
    /// Scenario:
    ///  Delete an active instance, user does not have priviliges
    /// Expected result:
    ///  No changes are made to the instance
    /// Success criteria:
    ///   Forbidden is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_ActiveMissingRole_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(1, 1, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/e6efc10e-913b-4a81-a36a-02376f5f5678?hard=true"
        );
        HttpStatusCode actualStatusCode = response.StatusCode;

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, actualStatusCode);
    }

    /// <summary>
    /// Scenario:
    ///  Delete an archived instance, user has delete priviliges
    /// Expected result:
    ///   Instance is marked for hard delete.
    /// Success criteria:
    ///   True is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_ArchivedHasRole_ReturnsOk()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/3b67392f-36c6-42dc-998f-c367e771dcdd?hard=false"
        );
        HttpStatusCode actualStatusCode = response.StatusCode;
        string content = await response.Content.ReadAsStringAsync();
        bool actualResult = JsonConvert.DeserializeObject<bool>(content);

        // Assert
        Assert.True(actualResult);
        Assert.Equal(HttpStatusCode.OK, actualStatusCode);
    }

    /// <summary>
    /// Scenario:
    ///  Delete an archived instance, user does not have priviliges
    /// Expected result:
    ///  No changes are made to the instance
    /// Success criteria:
    ///   Forbidden is returned for the http request.
    /// </summary>
    [Fact]
    public async Task Delete_ArchivedMissingRole_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(1, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/367a5e5a-12c6-4a74-b72b-766d95f859b0?hard=false"
        );
        HttpStatusCode actualStatusCode = response.StatusCode;

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, actualStatusCode);
    }

    /// <summary>
    /// Scenario:
    /// End user system tries to soft delete an instance, but GetApplicationOrErrorAsync throws an exception
    /// Expected:
    /// No changes are made to the instance
    /// Success:
    /// Internal server error is returned
    /// </summary>
    [Fact]
    public async Task Delete_GetApplicationOrErrorAsyncThrowsException_ReturnsInternalServerError()
    {
        // Arrange
        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((null, new ServiceError(500, "Something went wrong")));

        HttpClient client = GetTestClient(applicationServiceMock: applicationService);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 3)
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/sbl/instances/1337/367a5e5a-12c6-4a74-b72b-766d95f859b0?hard=false"
        );
        HttpStatusCode actualStatusCode = response.StatusCode;

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, actualStatusCode);
    }

    /// <summary>
    /// Scenario:
    ///  End user system tries to soft delete an instance, but GetApplicationOrErrorAsync returns app info error
    ///  Expected:
    ///  Returns status Not Found.
    ///  Success:
    ///  Status Not Found is returned.
    ///  </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstance_GetApplicationOrErrorAsyncReturnsErrorNotFound_ReturnsStatusNotFound()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "367a5e5a-12c6-4a74-b72b-766d95f859b0";

        string requestUri = $"{BasePath}/sbl/instances/{instanceOwnerId}/{instanceGuid}";

        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((null, new ServiceError(404, "Not found")));

        HttpClient client = GetTestClient(applicationServiceMock: applicationService);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    /// End user system tries to soft delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays
    /// Expected:
    /// Returns status forbidden.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_EndUserSoftDeletesInstancePreventedFromDeletion_ReturnsStatusForbidden(
        bool hard
    )
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "3f7fcd91-114e-4da1-95b6-72115f34945c";
        string requestUri = $"{BasePath}/sbl/instances/{instanceOwnerId}/{instanceGuid}";

        if (hard)
        {
            requestUri += "?hard=true";
        }

        var archived = DateTime.Parse("2024-04-29T13:53:06.117891Z");
        int daysSinceArchived = (int)(DateTime.UtcNow - archived).TotalDays;

        Application application = new() { PreventInstanceDeletionForDays = daysSinceArchived + 10 }; // Prevent deletion for longer than the instance has been archived
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock
            .Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((application, null));

        HttpClient client = GetTestClient(applicationServiceMock: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "Instance cannot be deleted yet due to application restrictions.",
            responseMessage
        );
    }

    /// <summary>
    /// Scenario:
    ///  End user system tries to soft delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays, but the instance is not archived
    ///  Expected:
    ///  Returns success and deleted instance
    ///  </summary>
    [Fact]
    public async Task Delete_EndUserSoftDeletesInstancePreventedFromDeletion_InstanceNotArchived_ReturnsSuccess()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "7e6cc8e2-6cd4-4ad4-9ce8-c37a767677b5";
        string requestUri = $"{BasePath}/sbl/instances/{instanceOwnerId}/{instanceGuid}";

        Application application = new() { PreventInstanceDeletionForDays = 30 };
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock
            .Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((application, null));

        HttpClient client = GetTestClient(applicationServiceMock: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bool.Parse(responseJson));
    }

    /// <summary>
    /// Test case: End user system tries to hard delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays, but the instance is not archived
    /// Expected: Returns success and deleted instance
    /// </summary>
    /// <summary>
    /// Scenario:
    /// End user system tries to hard delete an instance that is prevented from being deleted by PreventInstanceDeletionForDays, but the instance is not archived
    /// Expected:
    /// Returns success and deleted instance
    /// </summary>
    [Fact]
    public async Task Delete_EndUserHardDeletesInstancePreventedFromDeletion_InstanceNotArchived_ReturnsSuccess()
    {
        // Arrange
        int instanceOwnerId = 1337;
        string instanceGuid = "367a5e5a-12c6-4a74-b72b-766d95f859b0";
        string requestUri = $"{BasePath}/sbl/instances/{instanceOwnerId}/{instanceGuid}";

        requestUri += "?hard=true";

        Application application = new() { PreventInstanceDeletionForDays = 30 };
        Mock<IApplicationService> applicationServiceMock = new();
        applicationServiceMock
            .Setup(x => x.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((application, null));

        Instance instance = new()
        {
            Status = new InstanceStatus(),
            AppId = "org123/app456",
            Id = "org123/app456",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
        };

        HttpClient client = GetTestClient(applicationServiceMock: applicationServiceMock);
        string token = PrincipalUtil.GetToken(1337, 1337);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(requestUri);
        string responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bool.Parse(responseJson));
    }

    [Fact]
    public async Task GetMessageBoxInstanceEvents_AllEventTypesIncludedInSearch()
    {
        // Arrange
        string[] extepctedEventTypes =
        {
            "Created",
            "Deleted",
            "Undeleted",
            "Saved",
            "Submited",
            "SubstatusUpdated",
            "Signed",
            "SentToSign",
        };

        Mock<IInstanceEventRepository> repoMock = new();
        repoMock
            .Setup(rm =>
                rm.ListInstanceEvents(
                    It.IsAny<Guid>(),
                    It.Is<string[]>(eventTypes => !extepctedEventTypes.Except(eventTypes).Any()),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()
                )
            )
            .ReturnsAsync(new List<InstanceEvent>());

        var sut = new MessageBoxInstancesController(null, repoMock.Object, null, null, null, null);

        // Act
        await sut.GetMessageBoxInstanceEvents(1606, Guid.NewGuid());

        // Assert
        repoMock.VerifyAll();
    }

    /// <summary>
    /// If simmilar events lie in the sorted list the most recent event should be persisted.
    /// Events close in time from different users are not considered duplicates.
    /// </summary>
    [Fact]
    public async Task GetMessageBoxInstanceEvents_DuplicateEventsRemoved()
    {
        // Arrange
        var eventA = new InstanceEvent
        {
            Created = DateTime.Parse("1994-06-16T11:06:59.0851832Z"),
            EventType = "Saved",
            User = new() { UserId = 1337 },
        };

        var eventB = new InstanceEvent
        {
            Created = DateTime.Parse("1994-06-16T11:07:59.0851832Z"),
            EventType = "Saved",
            User = new() { UserId = 1337 },
        };

        var eventC = new InstanceEvent
        {
            Created = DateTime.Parse("1994-06-16T11:08:02.0851832Z"),
            EventType = "Saved",
            User = new() { UserId = 2008 },
        };

        List<InstanceEvent> eventList = new() { eventA, eventB, eventC };

        Mock<IInstanceEventRepository> repoMock = new();
        repoMock
            .Setup(rm =>
                rm.ListInstanceEvents(
                    It.IsAny<Guid>(),
                    It.IsAny<string[]>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()
                )
            )
            .ReturnsAsync(eventList);

        var sut = new MessageBoxInstancesController(null, repoMock.Object, null, null, null, null);

        // Act
        var response =
            await sut.GetMessageBoxInstanceEvents(1606, Guid.NewGuid()) as OkObjectResult;
        var actual = response.Value as List<SblInstanceEvent>;

        // Assert
        Assert.Equal(2, actual.Count);
        Assert.Single(actual, e => e.User.UserId == 1337);
        Assert.Contains(
            actual,
            e => e.CreatedDateTime == DateTime.Parse("1994-06-16T11:07:59.0851832Z")
        );
        repoMock.VerifyAll();
    }

    /// <summary>
    /// Scenario:
    ///  Verify that the GetMessageBoxInstanceEvents method can handle and return a large number of events correctly.
    /// </summary>
    [Fact]
    public async Task GetMessageBoxInstanceEvents_LargeNumberOfEvents_ReturnsAllEvents()
    {
        // Arrange
        var largeNumberOfEvents = Enumerable
            .Range(1, 1000)
            .Select(i => new InstanceEvent
            {
                Created = DateTime.UtcNow,
                EventType = "Event",
                User = new() { UserId = i },
            })
            .ToList();
        var repoMock = new Mock<IInstanceEventRepository>();
        repoMock
            .Setup(rm =>
                rm.ListInstanceEvents(
                    It.IsAny<Guid>(),
                    It.IsAny<string[]>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()
                )
            )
            .ReturnsAsync(largeNumberOfEvents);

        var sut = new MessageBoxInstancesController(null, repoMock.Object, null, null, null, null);

        // Act
        var response =
            await sut.GetMessageBoxInstanceEvents(1606, Guid.NewGuid()) as OkObjectResult;
        var actual = response.Value as List<SblInstanceEvent>;

        // Assert
        Assert.Equal(largeNumberOfEvents.Count, actual.Count);
    }

    [Fact]
    public async Task Delete_ProcessStatusConflict_ReturnsConflictWithoutEvent()
    {
        const ProcessStatus currentStatus = ProcessStatus.Processing;
        const int partyId = 1337;
        Guid instanceGuid = Guid.NewGuid();
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Id = $"{partyId}/{instanceGuid}";
        instance.Status ??= new InstanceStatus();
        InstanceInternal internalInstance = InstanceInternalTestFactory.Create(
            instance,
            [],
            InternalId: 42,
            versions: new StorageVersions(7, 11)
        );
        Mock<IInstanceRepository> repository = new();
        repository
            .Setup(instanceRepository =>
                instanceRepository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(internalInstance);
        repository
            .Setup(instanceRepository =>
                instanceRepository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    null,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(currentStatus));
        Mock<IInstanceEventRepository> instanceEventRepository = new(MockBehavior.Strict);
        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(service => service.GetApplicationOrErrorAsync(internalInstance.AppId))
            .ReturnsAsync((new Application { Id = internalInstance.AppId }, null));
        MessageBoxInstancesController controller = new(
            repository.Object,
            instanceEventRepository.Object,
            Mock.Of<ITextRepository>(),
            Mock.Of<IApplicationRepository>(),
            Mock.Of<IAuthorization>(),
            applicationService.Object
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        ActionResult result = await controller.Delete(
            instanceGuid,
            partyId,
            hard: false,
            CancellationToken.None
        );

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains(
            currentStatus.ToString().ToLowerInvariant(),
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
        instanceEventRepository.VerifyNoOtherCalls();
    }

    private HttpClient GetTestClient(
        Mock<IInstanceRepository> instanceRepositoryMock = null,
        Mock<IApplicationService> applicationServiceMock = null,
        bool enableWolverine = false
    )
    {
        // No setup required for these services. They are not in use by the MessageBoxInstancesController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();

        HttpClient client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    if (instanceRepositoryMock != null)
                    {
                        services.AddSingleton(instanceRepositoryMock.Object);
                    }

                    if (applicationServiceMock != null)
                    {
                        services.AddSingleton(applicationServiceMock.Object);
                    }

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.Configure<WolverineSettings>(opts =>
                    {
                        opts.EnableSending = enableWolverine;
                    });
                });
            })
            .CreateClient();

        return client;
    }
}
