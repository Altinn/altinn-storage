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
            
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_TestViaController_ReturnsOK()
        {
            var client = GetTestClient();
            Mock<IHttpWrapper> mock = new Mock<IHttpWrapper>();
            HttpResponseMessage httpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            mock.Setup(s => s.SendAsync(It.IsAny<HttpRequestMessage>())).ReturnsAsync(httpResponse);
            mock.Setup(s => s.AssignHttpClientSettings(It.IsAny<Uri>(), It.IsAny<TimeSpan>()));
            var cli = new SblBridgeController(null, new CorrespondenceClient(mock.Object, "https://localhost:88/"));

            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var result = await cli.SyncAltinn3CorrespondenceEvent(new Storage.Models.CorrespondenceEventSync()
            {
                CorrespondenceId = 2674,
                PartyId = 223,
                EventTimeStamp = dateTimeOffset,
                EventType = "read"
            });

            var x = result as Microsoft.AspNetCore.Mvc.OkResult;
            Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status200OK, x.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_TestViaController_FailsOncommunication_Returns502BadGateway()
        {
            var client = GetTestClient();
            Mock<IHttpWrapper> mock = new Mock<IHttpWrapper>();
            mock.Setup(s => s.SendAsync(It.IsAny<HttpRequestMessage>())).Throws(new HttpRequestException("Test HttpRequestException"));
            mock.Setup(s => s.AssignHttpClientSettings(It.IsAny<Uri>(), It.IsAny<TimeSpan>()));
            var cli = new SblBridgeController(null, new CorrespondenceClient(mock.Object, "https://localhost:88/"));

            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var result = await cli.SyncAltinn3CorrespondenceEvent(new Storage.Models.CorrespondenceEventSync()
            {
                CorrespondenceId = 2674,
                PartyId = 223,
                EventTimeStamp = dateTimeOffset,
                EventType = "read"
            });

            var x = result as Microsoft.AspNetCore.Mvc.ObjectResult;

            Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, x.StatusCode);
        }

        [Fact]
        public async Task SyncAltinn3CorrespondenceEvent_TestViaController_Timeout_Returns504GatewayTimeout()
        {
            var client = GetTestClient();
            Mock<IHttpWrapper> mock = new Mock<IHttpWrapper>();
            mock.Setup(s => s.SendAsync(It.IsAny<HttpRequestMessage>())).Throws(new TaskCanceledException("Test TaskCanceledException"));
            mock.Setup(s => s.AssignHttpClientSettings(It.IsAny<Uri>(), It.IsAny<TimeSpan>()));
            var cli = new SblBridgeController(null, new CorrespondenceClient(mock.Object, "https://localhost:88/"));

            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2025, 05, 01, 11, 0, 0, TimeSpan.FromHours(0));

            var result = await cli.SyncAltinn3CorrespondenceEvent(new Storage.Models.CorrespondenceEventSync()
            {
                CorrespondenceId = 2674,
                PartyId = 223,
                EventTimeStamp = dateTimeOffset,
                EventType = "read"
            });

            var x = result as Microsoft.AspNetCore.Mvc.ObjectResult;

            Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status504GatewayTimeout, x.StatusCode);
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
