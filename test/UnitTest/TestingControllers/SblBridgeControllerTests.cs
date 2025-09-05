using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Utils;
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
    /// <summary>
    /// Test class for Process Controller. Focuses on authorization of requests.
    /// </summary>
    public class SblBridgeControllerTests : IClassFixture<TestApplicationFactory<SblBridgeController>>
    {
        private readonly TestApplicationFactory<SblBridgeController> _factory;

        public SblBridgeControllerTests(TestApplicationFactory<SblBridgeController> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RegisterAltinn3CorrespondenceRecipient_ValidPartyId_ReturnsOk()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsync("storage/api/v1/sblbridge/correspondencerecipient?partyId=1337", JsonContent.Create(new
            {
                partyId = 1337
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RegisterAltinn3CorrespondenceRecipient_InvalidPartyId_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsync("storage/api/v1/sblbridge/correspondencerecipient?partyId=0", JsonContent.Create(new
            {
                partyId = 0
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);           
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_InvalidCorrespondenceId_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var response = await client.PostAsync("storage/api/v1/sblbridge/synccorrespondenceevent", JsonContent.Create(new
            {
                correspondenceId = 0,
                partyId = 223,
                eventTimeStamp = dateTimeOffset,
                eventType = "read"
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_InvalidPartyId_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var response = await client.PostAsync("storage/api/v1/sblbridge/synccorrespondenceevent", JsonContent.Create(new
            {
                correspondenceId = 2674,
                partyId = 0,
                eventTimeStamp = dateTimeOffset,
                eventType = "read"
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_InvalidTimestamp_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = DateTimeOffset.MinValue;

            var response = await client.PostAsync("storage/api/v1/sblbridge/synccorrespondenceevent", JsonContent.Create(new
            {
                correspondenceId = 2674,
                partyId = 223,
                eventTimeStamp = dateTimeOffset,
                eventType = "read"
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_InvalidEventType_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var response = await client.PostAsync("storage/api/v1/sblbridge/synccorrespondenceevent", JsonContent.Create(new
            {
                correspondenceId = 2674,
                partyId = 223,
                eventTimeStamp = dateTimeOffset,
                eventType = "invalidRead"
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_ValidInput_ReturnsOK()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var response = await client.PostAsync("storage/api/v1/sblbridge/synccorrespondenceevent", JsonContent.Create(new
            {
                correspondenceId = 2674,
                partyId = 223,
                eventTimeStamp = dateTimeOffset,
                eventType = "read"
            }));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
        
        private HttpClient GetTestClient()
        {
            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
                Mock<IMessageBus> busMock = new Mock<IMessageBus>();

                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<ICorrespondenceClient, CorrespondenceClientMock>();
                    services.AddSingleton<IPartiesWithInstancesClient, PartiesWithInstancesClientMock>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton(busMock.Object);
                });
            }).CreateClient();

            return client;
        }
    }
}
