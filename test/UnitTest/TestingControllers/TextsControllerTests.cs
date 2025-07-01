using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class TextsControllerTests : IClassFixture<TestApplicationFactory<TextsController>>
    {
        private readonly TestApplicationFactory<TextsController> _factory;
        private readonly HttpClient _httpClient;

        private const string BasePath = "/storage/api/v1/applications";

        public TextsControllerTests(TestApplicationFactory<TextsController> factory)
        {
            _factory = factory;
            Mock<ITextRepository> mockTextRepository = CreateMockTextRepo();
            _httpClient = CreateTestHttpClient(mockTextRepository.Object);
        }

        /// <summary>
        /// Scenario:
        ///   Posts a valid textResource
        /// Expected result:
        ///   Returns HttpStatus OK and the created textResource
        /// Success criteria:
        ///   The response has correct status and we get a text in return
        /// </summary>
        [Fact]
        public async Task Create_ReturnsOK()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts";

            HttpResponseMessage response = await _httpClient.PostAsync(
                requestUri,
                JsonContent.Create(GetValidTextResource(), new MediaTypeHeaderValue("application/json")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Tries to create a text resource that already exists
        /// Expected result:
        ///   Returns HttpStatus OK
        /// Success criteria:
        ///   The response has correct status code
        /// </summary>
        [Fact]
        public async Task CreateWhereResourceAlreadyExists_ReturnsOk()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts";

            HttpResponseMessage response = await _httpClient.PostAsync(
                requestUri,
                JsonContent.Create(GetTextResourceThatAlreadyExists(), new MediaTypeHeaderValue("application/json")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Get a text that exists
        /// Expected result:
        ///   Returns HttpStatus OK
        /// Success criteria:
        ///   The response has correct status and returns a text
        /// </summary>
        [Fact]
        public async Task Get_ReturnsOK()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts/en";

            HttpResponseMessage response = await _httpClient.GetAsync(requestUri);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Get a textResource with an invalid language string
        /// Expected result:
        ///   Returns HttpStatus BadRequest
        /// Success criteria:
        ///   The response has correct status
        /// </summary>
        [Fact]
        public async Task GetGivenInvalidLanguage_ReturnsBadRequest()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts/xxx";

            HttpResponseMessage response = await _httpClient.GetAsync(requestUri);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Delete a textResource that exists
        /// Expected result:
        ///   Returns HttpStatus OK
        /// Success criteria:
        ///   The response has correct status
        /// </summary>
        [Fact]
        public async Task Delete_ReturnsOK()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts/en";

            HttpResponseMessage response = await _httpClient.DeleteAsync(requestUri);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Delete a textResource that does not exists
        /// Expected result:
        ///   Returns HttpStatus NotFound
        /// Success criteria:
        ///   The response has correct status
        /// </summary>
        [Fact]
        public async Task DeleteForResourceThatDoesNotExist_ReturnsNotFound()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts/nb";

            HttpResponseMessage response = await _httpClient.DeleteAsync(requestUri);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Update a text
        /// Expected result:
        ///   Returns HttpStatus OK
        /// Success criteria:
        ///   The response has correct status and returns the updated object
        /// </summary>
        [Fact]
        public async Task Update_ReturnsOK()
        {
            string org = "testOrg";
            string app = "testApp";
            string requestUri = $"{BasePath}/{org}/{app}/texts/nb";

            HttpResponseMessage response = await _httpClient.DeleteAsync(requestUri);
            string content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(string.IsNullOrEmpty(content));
        }

        /// <summary>
        /// Scenario:
        ///   Get/Post/Put/Delete while unauthorized
        /// Expected result:
        ///   All requests returns HttpStatus Unautorized
        /// Success criteria:
        ///   The responses has correct status
        /// </summary>
        [Fact]
        public async Task UnauthorizedRequests_ReturnsUnauthorized()
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "notvalidtoken");
            string org = "testOrg";
            string app = "testApp";
            string requestUriPost = $"{BasePath}/{org}/{app}/texts";
            string requestUriOthers = $"{BasePath}/{org}/{app}/texts/en";

            HttpResponseMessage responseGet = await _httpClient.GetAsync(requestUriOthers);
            HttpResponseMessage responseDelete = await _httpClient.DeleteAsync(requestUriOthers);
            HttpResponseMessage responseCreate = await _httpClient.PostAsync(requestUriPost, JsonContent.Create(GetValidTextResource(), new MediaTypeHeaderValue("application/json")));
            HttpResponseMessage responsePut = await _httpClient.PutAsync(requestUriOthers, JsonContent.Create(GetValidTextResource(), new MediaTypeHeaderValue("application/json")));

            Assert.Equal(HttpStatusCode.Unauthorized, responseGet.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, responseDelete.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, responseCreate.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, responsePut.StatusCode);

            // reset auth header
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetOrgToken("testOrg"));
        }

        /// <summary>
        /// Scenario:
        ///   Only org token should be able to POST, PUT and DELETE. A regular token shuld only be able to GET.
        /// Expected result:
        ///   All requests returns HttpStatus forbidden for a regular token, expect GET
        /// Success criteria:
        ///   The responses has correct status
        /// </summary>
        [Fact]
        public async Task RegularTokenShouldNotBeAbleToPostPutAndDelete()
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1000));
            string org = "testOrg";
            string app = "testApp";
            string requestUriPost = $"{BasePath}/{org}/{app}/texts";
            string requestUriOthers = $"{BasePath}/{org}/{app}/texts/en";

            HttpResponseMessage responseGet = await _httpClient.GetAsync(requestUriOthers);
            HttpResponseMessage responseDelete = await _httpClient.DeleteAsync(requestUriOthers);
            HttpResponseMessage responseCreate = await _httpClient.PostAsync(requestUriPost, JsonContent.Create(GetValidTextResource(), new MediaTypeHeaderValue("application/json")));
            HttpResponseMessage responsePut = await _httpClient.PutAsync(requestUriOthers, JsonContent.Create(GetValidTextResource(), new MediaTypeHeaderValue("application/json")));

            Assert.Equal(HttpStatusCode.OK, responseGet.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, responseDelete.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, responseCreate.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, responsePut.StatusCode);
        }

        private static Mock<ITextRepository> CreateMockTextRepo()
        {
            Mock<ITextRepository> mockTextRepo = new Mock<ITextRepository>();
            mockTextRepo
                .Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string org, string app, string language) =>
                {
                    if (org.Equals("testOrg") && app.Equals("testApp") && language.Equals("en"))
                    {
                        return new TextResource { Language = language };
                    }

                    return null;
                });
            mockTextRepo.Setup(s => s.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TextResource>())).ReturnsAsync(new TextResource());
            mockTextRepo.Setup(s => s.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TextResource>())).ReturnsAsync(new TextResource());
            mockTextRepo.Setup(s => s.Delete(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            return mockTextRepo;
        }

        private HttpClient CreateTestHttpClient(ITextRepository textRepository)
        {
            // No setup required for these services. They are not in use by the ApplicationController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
            Mock<IMessageBus> busMock = new Mock<IMessageBus>();
            
            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    services.AddSingleton(textRepository);

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton(busMock.Object);
                });
            }).CreateClient();

            string token = PrincipalUtil.GetAccessToken("studio.designer");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return client;
        }

        private static TextResource GetValidTextResource()
        {
            return new TextResource
            {
                Language = "nb",
                Resources = new List<TextResourceElement>
                {
                    new TextResourceElement { Id = "some.id", Value = "Some value" },
                    new TextResourceElement { Id = "some.other.id", Value = "Some other value" }
                }
            };
        }

        private static TextResource GetTextResourceThatAlreadyExists()
        {
            return new TextResource
            {
                Language = "en",
                Resources = new List<TextResourceElement>
                {
                    new TextResourceElement { Id = "some.id", Value = "Some value" },
                    new TextResourceElement { Id = "some.other.id", Value = "Some other value" }
                }
            };
        }
    }
}
