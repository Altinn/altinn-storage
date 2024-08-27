using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Tests.Mocks;
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((Application)null);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new Exception());
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new Exception());
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
        ///   Post a valid Application instance that already exists and has different PartyTypesAllowed.
        /// Expected result:
        ///   Returns HttpStatus Created and the Application instance with updated PartyTypesAllowed but unchanged creation info.
        /// Success criteria:
        ///   The response has correct status and the returned application instance has updated PartyTypesAllowed while creation info remains unchanged.
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
            existingApp.Created = DateTime.UtcNow.AddDays(-1);
            existingApp.CreatedBy = "testUser";
            existingApp.PartyTypesAllowed = new PartyTypesAllowed { Person = true, SubUnit = true, Organisation = true, BankruptcyEstate = true };

            Application newApp = CreateApplication(org, appName);
            newApp.Created = DateTime.UtcNow;
            newApp.CreatedBy = "anotherTestUser";
            newApp.PartyTypesAllowed = new PartyTypesAllowed { Person = true, SubUnit = false, Organisation = false, BankruptcyEstate = true };

            Mock<IApplicationRepository> applicationRepository = new Mock<IApplicationRepository>();
            applicationRepository.Setup(e => e.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Application
            {
                Id = existingApp.Id,
                Org = existingApp.Org,
                Created = existingApp.Created,
                CreatedBy = existingApp.CreatedBy,
                PartyTypesAllowed = newApp.PartyTypesAllowed
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

            Assert.Equal(retrievedApp.Id, existingApp.Id);
            Assert.Equal(retrievedApp.Org, existingApp.Org);
            Assert.Equal(retrievedApp.Created, existingApp.Created);
            Assert.Equal(retrievedApp.CreatedBy, existingApp.CreatedBy);
            Assert.Equal(retrievedApp.PartyTypesAllowed.SubUnit, newApp.PartyTypesAllowed.SubUnit);
            Assert.Equal(retrievedApp.PartyTypesAllowed.Organisation, newApp.PartyTypesAllowed.Organisation);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(appInfo);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(appInfo);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(appInfo);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(originalApp);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(originalApp);
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
            applicationRepository.Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(originalApp);
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
              .Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync((Application)null);

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            applicationRepository.Verify(m => m.FindOne(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
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
              .Setup(s => s.FindOne(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(new Application { Id = "ttd/existing-app" });

            HttpClient client = GetTestClient(applicationRepository.Object);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            applicationRepository.Verify(m => m.FindOne(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private HttpClient GetTestClient(IApplicationRepository applicationRepository)
        {
            // No setup required for these services. They are not in use by the ApplicationController
            Mock<ISasTokenProvider> sasTokenProvider = new Mock<ISasTokenProvider>();
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();

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
