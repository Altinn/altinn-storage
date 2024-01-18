using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Tests.Mocks;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;

using AltinnCore.Authentication.JwtCookie;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Moq;

using Newtonsoft.Json;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class MessageBoxInstancesControllerTests : IClassFixture<TestApplicationFactory<MessageBoxInstancesController>>
    {
        private const string BasePath = "/storage/api/v1";
        private readonly TestApplicationFactory<MessageBoxInstancesController> _factory;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBoxInstancesControllerTests"/> class with the given <see cref="WebApplicationFactory{TStartup}"/>.
        /// </summary>
        /// <param name="factory">The <see cref="TestApplicationFactory{TStartup}"/> to use when setting up the test server.</param>
        public MessageBoxInstancesControllerTests(TestApplicationFactory<MessageBoxInstancesController> factory)
        {
            _factory = factory;
        }

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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage responseMessage = await client.GetAsync($"{BasePath}/sbl/instances/{instanceId}?language=en");

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

            string responseContent = await responseMessage.Content.ReadAsStringAsync();
            MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(responseContent);

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
            string instanceId = "1606/6323a337-26e7-4d40-89e8-f5bb3d80be3a";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            // Act
            HttpResponseMessage responseMessage = await client.GetAsync($"{BasePath}/sbl/instances/{instanceId}?language=en");

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

            string responseContent = await responseMessage.Content.ReadAsStringAsync();
            MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(responseContent);

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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1606, 3));

            // Act
            HttpResponseMessage responseMessage = await client.GetAsync($"{BasePath}/sbl/instances/{instanceId}?language=en");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, responseMessage.StatusCode);
        }

        [Fact]
        public async Task GetMessageBoxInstance_ArchivedInstanceCanBeCopied_UserHaveRights_InstanceIsSuccessfullyMappedAndReturned()
        {
            // Arrange
            string instanceId = "1337/07274f48-8313-4e2d-9788-bbdacef5a54e";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage responseMessage =
                await client.GetAsync($"{BasePath}/sbl/instances/{instanceId}?language=en");

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);

            string responseContent = await responseMessage.Content.ReadAsStringAsync();
            MessageBoxInstance actual = JsonConvert.DeserializeObject<MessageBoxInstance>(responseContent);

            Assert.True(actual.AllowNewCopy);
        }

        /// <summary>
        /// Scenario:
        ///   Restore a soft deleted instance in storage.
        /// Expected result:
        ///   The instance is restored.
        /// Success criteria:
        ///   True is returned for the http request.
        /// </summary>
        [Fact]
        public async Task Undelete_RestoreSoftDeletedInstance_ReturnsTrue()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PutAsync($"{BasePath}/sbl/instances/{1337}/da1f620f-1764-4f98-9f03-74e5e20f10fe/undelete", null);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            bool actualResult = JsonConvert.DeserializeObject<bool>(content);

            Assert.True(actualResult);
        }

        /// <summary>
        /// Scenario:
        ///   Restore a soft deleted instance in storage but user has too low authentication level.
        /// Expected result:
        ///   The instance is not restored and returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Undelete_UserHasTooLowAuthLv_ReturnsStatusForbidden()
        {
            // Arrange
            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(1337, 1337, 1);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PutAsync($"{BasePath}/sbl/instances/{1337}/cd41b024-f6b8-4ca7-9080-adc9eca5f0d1/undelete", null);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Restore a soft deleted instance in storage but response is deny.
        /// Expected result:
        ///   The instance is not restored and returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Undelete_ResponseIsDeny_ReturnsStatusForbidden()
        {
            // Arrange
            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(-1, 1);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PutAsync($"{BasePath}/sbl/instances/{1337}/cd41b024-f6b8-4ca7-9080-adc9eca5f0d1/undelete", null);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Restore a hard deleted instance in storage
        /// Expected result:
        ///   It should not be possible to restore a hard deleted instance
        /// Success criteria:
        ///   Response status is NotFound and the body contains correct reason.
        /// </summary>
        [Fact]
        public async Task Undelete_AttemptToRestoreHardDeletedInstance_ReturnsNotFound()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PutAsync($"{BasePath}/sbl/instances/1337/f888c42b-8749-41d6-8048-8fc28c70beaa/undelete", null);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();

            string expectedMsg = "Instance was permanently deleted and cannot be restored.";
            Assert.Equal(expectedMsg, content);
        }

        /// <summary>
        /// Scenario:
        ///   Non-existent instance to be restored
        /// Expected result:
        ///   Internal server error
        /// </summary>
        [Fact]
        public async Task Undelete_RestoreNonExistentInstance_ReturnsNotFound()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PutAsync($"{BasePath}/sbl/instances/1337/4be22ede-a16c-4a93-be7f-c529788d6a4c/undelete", null);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/08274f48-8313-4e2d-9788-bbdacef5a54e?hard=false");

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
            Mock<IInstanceEventRepository> instanceEventRepository = new Mock<IInstanceEventRepository>();
            instanceEventRepository.Setup(s => s.InsertInstanceEvent(It.IsAny<InstanceEvent>())).ReturnsAsync((InstanceEvent r) => r);

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(1337, 1337, 1);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a?hard=false");

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
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/6323a337-26e7-4d40-89e8-f5bb3d80be3a?hard=false");

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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/7a951b5b-ef96-4032-9273-f8d7651266f4?hard=true");

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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/d9a586ca-17ab-453d-9fc5-35eaadb3369b?hard=true");
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/e6efc10e-913b-4a81-a36a-02376f5f5678?hard=true");
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/3b67392f-36c6-42dc-998f-c367e771dcdd?hard=false");
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/sbl/instances/1337/367a5e5a-12c6-4a74-b72b-766d95f859b0?hard=false");
            HttpStatusCode actualStatusCode = response.StatusCode;

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, actualStatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Search instances for an instance owner without token
        /// Expected:
        ///  User is not able to query instances.
        /// Success:
        ///   Unauthorized is returned.
        /// </summary>
        [Fact]
        public async Task Post_Search_MissingToken_ReturnsForbidden()
        {
            // Arrange
            HttpClient client = GetTestClient();
            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1337);

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, responseMessage.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances for a given partyId and appId
        /// Expected:
        ///  There is a match for active, archived and soft deleted instances
        /// Success:
        ///  List of instances is returned
        /// </summary>
        [Fact]
        public async Task Post_Search_FilterOnAppId_ReturnsActiveArchivedAndDeletedInstances()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1600, 3));
            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.AppId = "ttd/steffens-2020-v2";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));
            string content = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actualResult = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(1, actualResult.Count(i => i.DeleteStatus == DeleteStatusType.SoftDeleted));
            Assert.Equal(1, actualResult.Count(i => i.ProcessCurrentTask == "FormFilling"));
            Assert.Equal(1, actualResult.Count(i => i.ProcessCurrentTask == "Archived" && i.DeleteStatus == DeleteStatusType.Default));
        }

        /// <summary>
        /// Scenario:
        ///  Search instances for a given partyId and appId
        /// Expected:
        ///  There are two matches, but one is a hard deleted instance
        /// Success:
        ///  Hard deleted instances are not included in the response.
        /// </summary>
        [Fact]
        public async Task Post_Search_FilterOnAppId_HardDeletedInstancesAreExcludedFromResult()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1606, 3));
            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.AppId = "ttd/complete-test";
            queryModel.Language = "en";

            int expectedCount = 1;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync(
                $"{BasePath}/sbl/instances/search",
                JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            string content = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actualResult = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(expectedCount, actualResult.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances based on unknown search parameter
        /// Expected:
        ///  Query response contains an exception.
        /// Success:
        ///  Bad request is returned
        /// </summary>
        [Fact]
        public async Task Post_Search_FilterOnUnknownParameter_BadRequestIsReturned()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));
            MessageBoxQueryModel queryModel = new MessageBoxQueryModel();

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, responseMessage.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with filter to only include active.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle.
        /// Success:
        ///  isSoftDeleted and isArchived are set to false.
        /// </summary>
        [Fact]
        public async Task Post_Search_IncludeActive_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            string expectedIsSoftDeleted = "false";
            string expectedIsArchived = "false";
            int expectedParamCount = 5;
            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));
            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.IncludeActive = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            Assert.True(actual.ContainsKey("sortBy"));
            actual.TryGetValue("status.isSoftDeleted", out StringValues actualIsSoftDeleted);
            Assert.Equal(expectedIsSoftDeleted, actualIsSoftDeleted[0]);
            actual.TryGetValue("status.isArchived", out StringValues actualIsArchived);
            Assert.Equal(expectedIsArchived, actualIsArchived[0]);
            Assert.Equal(expectedParamCount, actual.Keys.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with filter to only include archived.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle.
        /// Success:
        ///  isSoftDeleted is set to false and isArchived is set to true.
        /// </summary>
        [Fact]
        public async Task Post_Search_IncludeArchived_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            string expectedIsSoftDeleted = "false";
            string expectedIsArchived = "true";
            int expectedParamCount = 5;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.IncludeArchived = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            actual.TryGetValue("status.isSoftDeleted", out StringValues actualIsSoftDeleted);
            Assert.Equal(expectedIsSoftDeleted, actualIsSoftDeleted[0]);
            actual.TryGetValue("status.isArchived", out StringValues actualIsArchived);
            Assert.Equal(expectedIsArchived, actualIsArchived[0]);
            Assert.Equal(expectedParamCount, actual.Keys.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with all include filters set
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle.
        /// Success:
        ///  No new parameters are included, and the "includeX" parameters are removed.
        /// </summary>
        [Fact]
        public async Task Post_Search_IncludeAllStates_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);
            int expectedParamCount = 3;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.IncludeArchived = true;
            queryModel.IncludeActive = true;
            queryModel.IncludeDeleted = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            Assert.Equal(expectedParamCount, actual.Keys.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with filter to include archived and deleted instances.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle.
        /// Success:
        ///  isArchivedOrSoftDeleted is set to true.
        /// </summary>
        [Fact]
        public async Task Post_Search_IncludeArchivedAndDeleted_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            int expectedParamCount = 4;
            string expectedSortBy = "desc:lastChanged";

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.IncludeArchived = true;
            queryModel.IncludeDeleted = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            actual.TryGetValue("status.isArchivedOrSoftDeleted", out StringValues actualIsArchivedOrSoftDeleted);
            Assert.True(bool.Parse(actualIsArchivedOrSoftDeleted[0]));
            actual.TryGetValue("sortBy", out StringValues actualSortBy);
            Assert.Equal(expectedSortBy, actualSortBy[0]);
            Assert.Equal(expectedParamCount, actual.Keys.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with filter to include active and deleted instances.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle.
        /// Success:
        ///  isActiveOrSoftDeleted is set to true.
        /// </summary>
        [Fact]
        public async Task Post_Search_IncludeActivedAndDeleted_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            int expectedParamCount = 4;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.IncludeActive = true;
            queryModel.IncludeDeleted = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            actual.TryGetValue("status.isActiveOrSoftDeleted", out StringValues actualIsArchivedOrSoftDeleted);
            Assert.True(bool.Parse(actualIsArchivedOrSoftDeleted[0]));
            Assert.Equal(expectedParamCount, actual.Keys.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with filter to on search string and appId.
        /// Expected:
        ///  There is no overlap between search string and provided appId.
        /// Success:
        ///  Empty list is returned.
        /// </summary>
        [Fact]
        public async Task Post_Search_SearchStringDoesNotMatchAppId_EmptyListIsReturned()
        {
            // Arrange
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync((InstanceQueryResponse)null);

            int expectedCount = 0;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.AppId = "ttd/endring-av-navn";
            queryModel.SearchString = "karpeDiem";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));
            string responseContent = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actual = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(responseContent);

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(expectedCount, actual.Count);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with a search string that doesn't match any app title
        /// Expected:
        ///  No applicationId is retrieved and the repository call is never called.
        /// Success:
        ///  Empty list is returned.
        /// </summary>
        [Fact]
        public async Task Post_Search_SearchStringDoesNotMatchAnyApp_NoCallToRepository()
        {
            // Arrange
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync((InstanceQueryResponse)null);

            int expectedCount = 0;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.SearchString = "karpeDiem";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));
            string responseContent = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actual = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(responseContent);

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(expectedCount, actual.Count);
            instanceRepositoryMock.Verify(
                ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Never);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with search string as filter.
        /// Expected:
        ///  A matching application is found and query parameters are transformed accordingly.
        /// Success:
        ///  SearchString is removed and appId is included in query string
        /// </summary>
        [Fact]
        public async Task Post_Search_MatchFoundForSearchString_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);
            string expectedAppId = "tdd/endring-av-navn";

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.SearchString = "navn";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("appId"));
            actual.TryGetValue("appId", out StringValues actualAppid);
            Assert.Equal(expectedAppId, actualAppid[0]);
            Assert.False(actual.ContainsKey("searchString"));
            instanceRepositoryMock.VerifyAll();
        }

        /// <summary>
        /// Scenario:
        ///  Search instances with search string as filter.
        /// Expected:
        ///  Two matching application are found and query parameters are transformed accordingly.
        /// Success:
        ///  SearchString is removed and appId is included in query string.
        /// </summary>
        [Fact]
        public async Task Post_Search_MultipleMatchesFoundForSearchString_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);
            int expectedCount = 3;

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);
            queryModel.SearchString = "TEST";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("appId"));
            actual.TryGetValue("appId", out StringValues actualAppid);
            Assert.Equal(expectedCount, actualAppid.Count);
            Assert.False(actual.ContainsKey("searchString"));
            instanceRepositoryMock.VerifyAll();
        }

        /// <summary>
        /// Scenario:
        ///  Search instances across parties
        /// Expected:
        ///  Both instanceOwner.partyIds are forwarded to instance repository.
        /// Success:
        ///  Instances for two parties are returned
        /// </summary>
        [Fact]
        public async Task Post_Search_MultiplePartyIds_InstancesForBothIdsReturned()
        {
            // Arrange
            int expectedCount = 3;
            int expectedDistinctInstanceOwners = 2;
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1600, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.InstanceOwnerPartyIdList.Add(1000);
            queryModel.AppId = "ttd/complete-test";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            string content = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actual = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(content);
            int distinctInstanceOwners = actual.Select(i => i.InstanceOwnerId).Distinct().Count();

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(expectedCount, actual.Count);
            Assert.Equal(expectedDistinctInstanceOwners, distinctInstanceOwners);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances based on archive reference. No state filter included.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle. Excluding all active instances.
        /// Success:
        ///  isArchivedOrSoftDeleted is set to true.
        /// </summary>
        [Fact]
        public async Task Post_Search_ArchiveReferenceNoStateFilter_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1600, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.ArchiveReference = "bdb2a09da7ea";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            actual.TryGetValue("status.isArchivedOrSoftDeleted", out StringValues actualIsArchivedOrSoftDeleted);
            Assert.True(bool.Parse(actualIsArchivedOrSoftDeleted[0]));
        }

        /// <summary>
        /// Scenario:
        ///  Search instances based on archive reference. Include active and soft deleted selected.
        /// Expected:
        ///  Query parameters are mapped to parameters that instanceRepository can handle. Excluding all active instances.
        /// Success:
        ///  isArchived is set to true. isSoftDeleted is set to false.
        /// </summary>
        [Fact]
        public async Task Post_Search_ArchiveReferenceIncludeActiveAndSoftDeleted_OriginalQuerySuccesfullyConverted()
        {
            // Arrange
            Dictionary<string, StringValues> actual = new Dictionary<string, StringValues>();
            Mock<IInstanceRepository> instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<Dictionary<string, StringValues>>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Dictionary<string, StringValues>, string, int>((query, cont, size) => { actual = query; })
                .ReturnsAsync((InstanceQueryResponse)null);

            HttpClient client = GetTestClient(instanceRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1600, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.ArchiveReference = "bdb2a09da7ea";
            queryModel.IncludeActive = true;
            queryModel.IncludeDeleted = true;

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.True(actual.ContainsKey("instanceOwner.partyId"));
            actual.TryGetValue("status.isSoftDeleted", out StringValues actualIsArchived);
            Assert.True(bool.Parse(actualIsArchived[0]));
        }

        /// <summary>
        /// Scenario:
        ///  Search instances based on appId.
        /// Expected:
        ///  VisibleAfter not reached for an instance, this is removed from the response.
        /// Success:
        ///  Single instance is returned.
        /// </summary>
        [Fact]
        public async Task Post_Search_VisibleDateNotReached_InstanceRemovedFromResponse()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1606, 3));

            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1606);

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync($"{BasePath}/sbl/instances/search?instanceOwner.partyId=1606", JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            string content = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actual = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(content);

            // Assert
            Assert.Single(actual);
        }

        /// <summary>
        /// Scenario:
        ///  Search instances for a given partyId and appId
        /// Expected:
        ///  There is a single match on an instanced in the signing task
        /// Success:
        ///  Messagebox instance contains authorizedForSign true.
        /// </summary>
        [Fact]
        public async Task Post_Search_InstanceInSigningStage_UserAuthorizedToSign()
        {
            // Arrange
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1600, 3));
            MessageBoxQueryModel queryModel = GetMessageBoxQueryModel(1600);
            queryModel.AppId = "ttd/signing-app";

            // Act
            HttpResponseMessage responseMessage = await client.PostAsync(
                $"{BasePath}/sbl/instances/search",
                JsonContent.Create(queryModel, new MediaTypeHeaderValue("application/json")));

            string content = await responseMessage.Content.ReadAsStringAsync();
            List<MessageBoxInstance> actualResult = JsonConvert.DeserializeObject<List<MessageBoxInstance>>(content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Single(actualResult);
            Assert.True(actualResult[0].AuthorizedForSign);
        }

        [Fact]
        public async Task GetMessageBoxInstanceEvents_AllEventTypesIncludedInSearch()
        {
            // Arrange            
            string[] extepctedEventTypes = { "Created", "Deleted", "Undeleted", "Saved", "Submited", "SubstatusUpdated", "Signed", "SentToSign" };

            Mock<IInstanceEventRepository> repoMock = new();
            repoMock
                .Setup(rm => rm.ListInstanceEvents(
                    It.IsAny<string>(),
                    It.Is<string[]>(eventTypes => !extepctedEventTypes.Except(eventTypes).Any()),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()))
                .ReturnsAsync(new List<InstanceEvent>());

            var sut = new MessageBoxInstancesController(null, repoMock.Object, null, null, null);

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
                User = new()
                {
                    UserId = 1337
                }
            };

            var eventB = new InstanceEvent
            {
                Created = DateTime.Parse("1994-06-16T11:07:59.0851832Z"),
                EventType = "Saved",
                User = new()
                {
                    UserId = 1337
                }
            };

            var eventC = new InstanceEvent
            {
                Created = DateTime.Parse("1994-06-16T11:08:02.0851832Z"),
                EventType = "Saved",
                User = new()
                {
                    UserId = 2008
                }
            };

            List<InstanceEvent> eventList = new() { eventA, eventB, eventC };

            Mock<IInstanceEventRepository> repoMock = new();
            repoMock
                .Setup(rm => rm.ListInstanceEvents(
                    It.IsAny<string>(),
                    It.IsAny<string[]>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()))
                .ReturnsAsync(eventList);

            var sut = new MessageBoxInstancesController(null, repoMock.Object, null, null, null);

            // Act
            var response = await sut.GetMessageBoxInstanceEvents(1606, Guid.NewGuid()) as OkObjectResult;
            var actual = response.Value as List<SblInstanceEvent>;

            // Assert            
            Assert.Equal(2, actual.Count);
            Assert.Single(actual.Where(e => e.User.UserId == 1337));
            Assert.NotEmpty(actual.Where(e => e.CreatedDateTime == DateTime.Parse("1994-06-16T11:07:59.0851832Z")));
            repoMock.VerifyAll();
        }

        private static MessageBoxQueryModel GetMessageBoxQueryModel(int instanceOwnerPartyId)
        {
            return new MessageBoxQueryModel
            {
                InstanceOwnerPartyIdList = new List<int>
                {
                    instanceOwnerPartyId
                }
            };
        }

        private HttpClient GetTestClient(Mock<IInstanceRepository> instanceRepositoryMock = null)
        {
            // No setup required for these services. They are not in use by the MessageBoxInstancesController
            Mock<ISasTokenProvider> sasTokenProvider = new Mock<ISasTokenProvider>();
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();

            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    if (instanceRepositoryMock != null)
                    {
                        services.AddSingleton(instanceRepositoryMock.Object);
                    }

                    services.AddSingleton(sasTokenProvider.Object);
                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                });
            }).CreateClient();

            return client;
        }
    }
}
