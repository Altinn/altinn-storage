using System;
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
    public class InstanceEventsControllerTests : IClassFixture<TestApplicationFactory<InstanceEventsController>>
    {
        private readonly TestApplicationFactory<InstanceEventsController> _factory;

        public InstanceEventsControllerTests(TestApplicationFactory<InstanceEventsController> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Add a new event to an instance.
        /// </summary>
        [Fact]
        public async Task Post_CreateNewEvent_ReturnsCreated()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(3, 1337);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            InstanceEvent instance = new InstanceEvent
            {
                InstanceId = "3c42ee2a-9464-42a8-a976-16eb926bd20a"
            };

            // Act
            JsonContent content = JsonContent.Create(instance, new MediaTypeHeaderValue("application/json"));
            HttpResponseMessage response = await client.PostAsync(requestUri, content);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        /// <summary>
        /// Test case: User has to low authentication level. 
        /// Expected: Returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Post_UserHasToLowAuthLv_ReturnStatusForbidden()
        {
            // Arrange
            string requestUri = $"storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(3, 1337, 0);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            InstanceEvent instance = new InstanceEvent();

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(instance, new MediaTypeHeaderValue("application/json")));

            if (response.StatusCode.Equals(HttpStatusCode.InternalServerError))
            {
                string serverContent = await response.Content.ReadAsStringAsync();
                Assert.Equal("Hei", serverContent);
            }

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary>
        /// Test case: Response is deny. 
        /// Expected: Returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Post_ResponseIsDeny_ReturnStatusForbidden()
        {
            // Arrange
            string requestUri = $"storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(-1, 1);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            InstanceEvent instance = new InstanceEvent();

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
        public async Task GetOne_UserHasToLowAuthLv_ReturnStatusForbidden()
        {
            // Arrange
            string eventGuid = "c8a44353-114a-48fc-af8f-b85392793cb2";
            string requestUri = $"storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/{eventGuid}";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(3, 1337, 0);
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
        public async Task GetOne_ResponseIsDeny_ReturnStatusForbidden()
        {
            string eventGuid = "9f07c256-a344-490b-b42b-1c855a83f6fc";
            string requestUri = $"storage/api/v1/instances/1337/a6020470-2200-4448-bed9-ef46b679bdb8/events/{eventGuid}";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(-1, 1337);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary>
        /// Test case: User has to low authentication level. 
        /// Expected: Returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Get_UserHasToLowAuthLv_ReturnStatusForbidden()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(3, 1337, 0);
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
        public async Task Get_ResponseIsDeny_ReturnStatusForbidden()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(-1, 1);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        private HttpClient GetTestClient()
        {
            // No setup required for these services. They are not in use by the InstanceEventController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
            Mock<IMessageBus> busMock = new Mock<IMessageBus>();

            Environment.SetEnvironmentVariable("WolverineSettings__ServiceBusConnectionString", string.Empty);

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

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton(busMock.Object);
                });
            }).CreateClient();

            return client;
        }
    }
}
