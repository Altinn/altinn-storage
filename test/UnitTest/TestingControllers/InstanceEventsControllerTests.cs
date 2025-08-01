using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
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

        [Fact]
        public async Task Post_WhenInstanceEventInstanceIdNotSet_Returns400BadRequest()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(3, 1337);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            InstanceEvent instance = new InstanceEvent
            {
                InstanceId = null
            };

            // Act
            JsonContent content = JsonContent.Create(instance, new MediaTypeHeaderValue("application/json"));
            HttpResponseMessage response = await client.PostAsync(requestUri, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        [Fact]
        public async Task Post_CreateNewEventWithWolverineEnabledAndWolverineThrows_ReturnsCreated()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            Mock<IMessageBus> messageBusMock = new Mock<IMessageBus>();
            messageBusMock.Setup(s => s.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
                .ThrowsAsync(new Exception());

            HttpClient client = GetTestClient(enableWolverine: true);
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

        [Fact]
        public async Task Post_CreateNewEventWithWolverineEnabled_ReturnsCreated()
        {
            // Arrange
            string requestUri = "storage/api/v1/instances/1337/3c42ee2a-9464-42a8-a976-16eb926bd20a/events/";

            Mock<IMessageBus> messageBusMock = new();
            SyncInstanceToDialogportenCommand savedCommand = null; // To be set properly by mock callback

            messageBusMock
                .Setup(s => s.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
                .Callback<SyncInstanceToDialogportenCommand, DeliveryOptions>((cmd, opt) => savedCommand = cmd)
                .Returns(ValueTask.CompletedTask);

            HttpClient client = GetTestClient(messageBusMock, enableWolverine: true);
            string token = PrincipalUtil.GetToken(3, 1337);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            InstanceEvent instance = new InstanceEvent
            {
                InstanceId = "3c42ee2a-9464-42a8-a976-16eb926bd20a"
            };

            var expectedCreated = DateTime.Parse("2019-07-31T09:57:23.4729995Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

            // Act
            JsonContent content = JsonContent.Create(instance, new MediaTypeHeaderValue("application/json"));
            HttpResponseMessage response = await client.PostAsync(requestUri, content);

            // Assert
            messageBusMock.VerifyAll();
            
            Assert.Equal("tdd/endring-av-navn", savedCommand.AppId);
            Assert.Equal("1337", savedCommand.PartyId);
            Assert.Equal("20475edd-dc38-4ae0-bd64-1b20643f506c", savedCommand.InstanceId);
            Assert.Equal(expectedCreated, savedCommand.InstanceCreatedAt);
            Assert.False(savedCommand.IsMigration);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        /// <summary>
        /// Test case: User has to low authentication level. 
        /// Expected: Returns status forbidden.
        /// </summary>
        [Fact]
        public async Task Post_UserHasTooLowAuthLv_ReturnStatusForbidden()
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

        private HttpClient GetTestClient(Mock<IMessageBus> messageBusMock = null, bool enableWolverine = false)
        {
            // No setup required for these services. They are not in use by the InstanceEventController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
            Mock<IMessageBus> busMock = messageBusMock ?? new Mock<IMessageBus>();

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
                    services.Configure<WolverineSettings>(opts =>
                    {
                        opts.EnableSending = enableWolverine;
                    });
                });
            }).CreateClient();

            return client;
        }
    }
}
