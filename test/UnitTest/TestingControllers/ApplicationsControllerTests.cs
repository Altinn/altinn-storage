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
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;

using AltinnCore.Authentication.JwtCookie;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using Newtonsoft.Json;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class ApplicationsControllerTests : IClassFixture<TestApplicationFactory<ApplicationsController>>
    {
        private const string BasePath = "/storage/api/v1";

        private readonly TestApplicationFactory<ApplicationsController> _factory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationsControllerTests"/> class with the given <see cref="WebApplicationFactory{TStartup}"/>.
        /// </summary>
        /// <param name="factory">The <see cref="TestApplicationFactory{TStartup}"/> to use when setting up the test server.</param>
        public ApplicationsControllerTests(TestApplicationFactory<ApplicationsController> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Testing that the <see cref="ApplicationsController.IsValidAppId"/> operation successfully identifies valid and invalid app id values.
        /// </summary>
        [Fact]
        public void IsValidAppId_SuccessfullyIdentifiesValidAndInvalidAppIdValues()
        {
            ApplicationsController appController = new ApplicationsController(null, null);

            Assert.True(appController.IsValidAppId("test/a234"));
            Assert.True(appController.IsValidAppId("sp1/ab23"));
            Assert.True(appController.IsValidAppId("multipledash/a-b-234"));

            Assert.False(appController.IsValidAppId("2orgstartswithnumber/b234"));
            Assert.False(appController.IsValidAppId("UpperCaseOrg/x234"));
            Assert.False(appController.IsValidAppId("org-with-dash/x234"));
            Assert.False(appController.IsValidAppId("morethanoneslash/a2/34"));
            Assert.False(appController.IsValidAppId("test/UpperCaseApp"));
            Assert.False(appController.IsValidAppId("testonlynumbersinapp/42"));
        }

        /// <summary>
        /// Scenario:
        ///   Post a simple but valid Application instance.
        /// Expected result:
        ///   Returns HttpStatus Created and the Application instance.
        /// Success criteria:
        ///   The response has correct status and the returned application instance has been populated with an empty list of data type.
        /// </summary>
        [Fact]
        public async Task Post_GivenValidApplication_ReturnsStatusCreatedAndCorrectData()
        {
            // Arrange
            string org = "test";
            string appName = "app20";
            string requestUri = $"{BasePath}/applications?appId={org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Application)null);
            applicationRepository.Setup(s => s.Create(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(appInfo, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Application application = JsonConvert.DeserializeObject(content, typeof(Application)) as Application;

            Assert.NotNull(application);
            Assert.NotNull(application.DataTypes);
            Assert.Empty(application.DataTypes);
        }

        /// <summary>
        /// Scenario:
        ///   Post a simple, valid Application instance but client has incorrect scope.
        /// Expected result:
        ///   Returns HttpStatus Forbidden and no Application instance get returned.
        /// </summary>
        [Fact]
        public async Task Post_ClientWithIncorrectScope_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app20";
            string requestUri = $"{BasePath}/applications?appId={org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception());
            applicationRepository.Setup(s => s.Create(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetOrgToken(org: "testOrg", scope: "altinn:invalidScope");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(appInfo, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Post a simple but valid Application instance but scope claim is empty.
        /// Expected result:
        ///   Returns HttpStatus Forbidden and no Application instance get returned.
        /// </summary>
        [Fact]
        public async Task Post_ClientWithEmptyScope_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app20";
            string requestUri = $"{BasePath}/applications?appId={org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception());
            applicationRepository.Setup(s => s.Create(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetOrgToken("testorg", scope: string.Empty);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(appInfo, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Post a simple application with an invalid id.
        /// Expected result:
        ///   Returns HttpStatus BadRequest with a reason phrase.
        /// Success criteria:
        ///   The response has correct status and the returned reason phrase has the correct keywords.
        /// </summary>
        [Fact]
        public async Task Post_GivenApplicationWithInvalidId_ReturnsStatusBadRequestWithMessage()
        {
            // Arrange
            string org = "TEST";
            string appName = "app";
            string requestUri = $"{BasePath}/applications?appId={org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(appInfo, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();

            Assert.Contains("not valid", content);
        }

        /// <summary>
        /// Scenario:
        ///   Post a valid Application instance that already exists but holds a different version ID.
        /// Expected result:
        ///   The response status is HttpStatusCode.Created.
        ///   The returned Application instance reflects the updated version ID.
        ///   The creation information (Created and CreatedBy) remains unchanged.
        /// Success Criteria:
        ///   The response has the correct status code.
        ///   The returned Application instance reflects the updated version ID while retaining the original creation information.
        /// </summary>
        [Fact]
        public async Task PostAndGet_ExistingApplication_ReturnsStatusCreatedAndCorrectData_CreationInfoUnchanged()
        {
            // Arrange
            string org = "test";
            string appName = "app20";
            string getUri = $"{BasePath}/applications/{org}/{appName}";
            string postUri = $"{BasePath}/applications?appId={org}/{appName}";

            Application existingApp = CreateApplication(org, appName);
            existingApp.Created = DateTime.UtcNow.AddDays(-10);
            existingApp.CreatedBy = "testUser";
            existingApp.VersionId = "v1.0.0";

            Application newApp = CreateApplication(org, appName);
            newApp.Created = DateTime.UtcNow;
            newApp.CreatedBy = "anotherTestUser";
            newApp.VersionId = "v1.0.1";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(e => e.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application
            {
                Id = newApp.Id,
                Org = newApp.Org,
                VersionId = newApp.VersionId,
                Created = existingApp.Created,
                CreatedBy = existingApp.CreatedBy
            });
            applicationRepository.Setup(e => e.Create(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage postResponse = await client.PostAsync(postUri, JsonContent.Create(newApp, new MediaTypeHeaderValue("application/json")));
            string postContent = await postResponse.Content.ReadAsStringAsync();
            Application createdApp = JsonConvert.DeserializeObject(postContent, typeof(Application)) as Application;

            HttpResponseMessage getResponse = await client.GetAsync(getUri);
            string getContent = await getResponse.Content.ReadAsStringAsync();
            Application retrievedApp = JsonConvert.DeserializeObject(getContent, typeof(Application)) as Application;

            // Assert
            Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

            Assert.NotNull(createdApp);
            Assert.Empty(createdApp.DataTypes);
            Assert.NotNull(createdApp.DataTypes);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            Assert.Equal(retrievedApp.Id, newApp.Id);
            Assert.Equal(retrievedApp.Org, newApp.Org);
            Assert.Equal(retrievedApp.VersionId, newApp.VersionId);
            Assert.Equal(retrievedApp.Created, existingApp.Created);
            Assert.Equal(retrievedApp.CreatedBy, existingApp.CreatedBy);
        }

        /// <summary>
        /// Scenario:
        ///   Put a valid Application instance that already exists but holds a different version ID.
        /// Expected Outcome:
        ///   The response status is HttpStatusCode.OK.
        ///   The returned Application instance reflects the updated version ID.
        ///   The creation information (Created and CreatedBy) remains unchanged.
        /// Success Criteria:
        ///   The response has the correct status code.
        ///   The returned Application instance reflects the updated version ID while retaining the original creation information.
        /// </summary>
        [Fact]
        public async Task PutAndGet_ExistingApplication_ReturnsStatusCreatedAndCorrectData_CreationInfoUnchanged()
        {
            // Arrange
            string org = "test";
            string appName = "app20";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application existingApp = CreateApplication(org, appName);
            existingApp.Created = DateTime.UtcNow.AddDays(-15);
            existingApp.CreatedBy = "testUser";
            existingApp.VersionId = "v1.0.0";

            Application newApp = CreateApplication(org, appName);
            newApp.Created = DateTime.UtcNow;
            newApp.CreatedBy = "anotherTestUser";
            newApp.VersionId = "v1.0.1";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(e => e.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application
            {
                Id = newApp.Id,
                Org = newApp.Org,
                VersionId = newApp.VersionId,
                Created = existingApp.Created,
                CreatedBy = existingApp.CreatedBy
            });
            applicationRepository.Setup(e => e.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);
            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage putResponse = await client.PutAsync(requestUri, JsonContent.Create(newApp, new MediaTypeHeaderValue("application/json")));

            HttpResponseMessage getResponse = await client.GetAsync(requestUri);
            string getContent = await getResponse.Content.ReadAsStringAsync();
            Application retrievedApp = JsonConvert.DeserializeObject(getContent, typeof(Application)) as Application;

            // Assert
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            Assert.Equal(retrievedApp.Id, newApp.Id);
            Assert.Equal(retrievedApp.Org, newApp.Org);
            Assert.Equal(retrievedApp.VersionId, newApp.VersionId);
            Assert.Equal(retrievedApp.Created, existingApp.Created);
            Assert.Equal(retrievedApp.CreatedBy, existingApp.CreatedBy);
        }

        /// <summary>
        /// Scenario:
        ///   Soft delete an existing application but empty appId claim in context.
        /// Expected result:
        ///   Returns HttpStatus Forbidden and application will not be updated
        /// </summary>
        [Fact]
        public async Task Delete_ClientWithEmptyAppId_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(appInfo);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetAccessToken(string.Empty);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Soft delete an existing application but incorrect appId claim in context.
        /// Expected result:
        ///   Returns HttpStatus Forbidden and application will not be updated
        /// </summary>
        [Fact]
        public async Task Delete_ClientWithIncorrectAppId_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(appInfo);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetAccessToken("studddio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Soft delete an existing application
        /// Expected result:
        ///   Returns HttpStatus Accepted and an updated application
        /// Success criteria:
        ///   The response has correct status code and the returned application has updated valid to date.
        /// </summary>
        [Fact]
        public async Task Delete_GivenExistingApplicationToSoftDelete_ReturnsStatusAcceptedWithUpdatedValidDateOnApplication()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application appInfo = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(appInfo);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Application application = JsonConvert.DeserializeObject(content, typeof(Application)) as Application;

            Assert.NotNull(application);
            Assert.True(application.ValidTo < DateTime.UtcNow);
        }

        /// <summary>
        /// Create an application, read one, update it and get it one more time.
        /// </summary>
        [Fact]
        public async Task GetAndUpdateApplication()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application originalApp = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(originalApp);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Application updatedApp = CreateApplication(org, appName);
            updatedApp.VersionId = "r34";
            updatedApp.PartyTypesAllowed = new PartyTypesAllowed { BankruptcyEstate = true };

            // Act
            HttpResponseMessage response = await client.PutAsync(requestUri, JsonContent.Create(updatedApp, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Application application = JsonConvert.DeserializeObject(content, typeof(Application)) as Application;

            Assert.NotNull(application);
            Assert.Equal("r34", application.VersionId);
            Assert.True(application.PartyTypesAllowed.BankruptcyEstate);
            Assert.False(application.PartyTypesAllowed.Person);
        }

        /// <summary>
        /// Create an application, read one, update it and get it one more time  but user has too low authentication level.
        /// </summary>
        [Fact]
        public async Task GetAndUpdateApplication_AuthLv0_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application originalApp = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(originalApp);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetToken(10001, 50001, 0);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Application updatedApp = CreateApplication(org, appName);
            updatedApp.VersionId = "r34";
            updatedApp.PartyTypesAllowed = new PartyTypesAllowed { BankruptcyEstate = true };

            // Act
            HttpResponseMessage response = await client.PutAsync(requestUri, JsonContent.Create(updatedApp, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Create an application, read one, update it and get it one more time  but response is deny.
        /// </summary>
        [Fact]
        public async Task GetAndUpdateApplication_ResponseIsDeny_ReturnsStatusForbidden()
        {
            // Arrange
            string org = "test";
            string appName = "app21";
            string requestUri = $"{BasePath}/applications/{org}/{appName}";

            Application originalApp = CreateApplication(org, appName);

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(originalApp);
            applicationRepository.Setup(s => s.Update(It.IsAny<Application>())).ReturnsAsync((Application app) => app);

            HttpClient client = GetTestClient(applicationRepository.Object);

            string token = PrincipalUtil.GetToken(-10001, 50001);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Application updatedApp = CreateApplication(org, appName);
            updatedApp.VersionId = "r34";
            updatedApp.PartyTypesAllowed = new PartyTypesAllowed { BankruptcyEstate = true };

            // Act
            HttpResponseMessage response = await client.PutAsync(requestUri, JsonContent.Create(updatedApp, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Get all applications returns 200.
        /// </summary>
        [Fact]
        public async Task GetAll_ReturnsOK()
        {
            // Arrange
            string requestUri = $"{BasePath}/applications";
            List<Application> expected = new List<Application>
            {
                CreateApplication("testorg", "testapp")
            };
            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(s => s.FindAll()).ReturnsAsync(expected);
            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Request all applications of an invalid app owner. Repository called is never called. Controller returns bad request.
        /// </summary>
        [Fact]
        public async Task GetMany_InvalidOrg_ReturnsBadRequest()
        {
            // Arrange
            string requestUri = $"{BasePath}/applications/test%20org";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Request all applications of a valid app owner. Repository called once and controller returns 200.
        /// </summary>
        [Fact]
        public async Task GetMany_ValidRequest_RepositoryIsCalledOnce()
        {
            // Arrange
            string requestUri = $"{BasePath}/applications/test";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            var returnValue = new List<Application>() { new Application { Id = "test/sailor" } };

            applicationRepository
                .Setup(s => s.FindByOrg(It.IsAny<string>()))
                .ReturnsAsync(returnValue);

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            applicationRepository.Verify(m => m.FindByOrg(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Request an application that does not exist. Repository called once. Controller returns not found. 
        /// </summary>
        [Fact]
        public async Task GetOne_NonExistingApp_ReturnsNotFound()
        {
            // Arrange
            string requestUri = $"{BasePath}/applications/ttd/non-exsisting-app";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository
              .Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Application)null);

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            applicationRepository.Verify(m => m.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Request an application that exists. Repository called once. Controller returns ok. 
        /// </summary>
        [Fact]
        public async Task GetOne_ExistingApp_ReturnsOk()
        {
            // Arrange
            string requestUri = $"{BasePath}/applications/ttd/exsisting-app";

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository
              .Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Application { Id = "ttd/existing-app" });

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            applicationRepository.Verify(m => m.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private HttpClient GetTestClient(IApplicationRepository applicationRepository)
        {
            // No setup required for these services. They are not in use by the ApplicationController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
            Mock<IMessageBus> busMock = new Mock<IMessageBus>();
            
            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    services.AddSingleton(applicationRepository);

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton(busMock.Object);
                });
            }).CreateClient();

            return client;
        }

        private static Application CreateApplication(string org, string appName)
        {
            Application appInfo = new Application
            {
                Id = $"{org}/{appName}",
                VersionId = "r33",
                Title = new Dictionary<string, string>(),
                Org = org,
            };

            appInfo.Title.Add("nb", "Tittel");

            return appInfo;
        }
    }
}
