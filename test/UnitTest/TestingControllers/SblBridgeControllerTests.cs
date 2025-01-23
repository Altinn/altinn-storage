using System.Net.Http;
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

            var response = await client.PostAsync("storage/api/v1/sblbridge/correspondencerecipient?partyId=1337", null);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RegisterAltinn3CorrespondenceRecipient_InvalidPartyId_ReturnsBadRequest()
        {
            var client = GetTestClient();
            string token = PrincipalUtil.GetOrgToken("foo", scope: "altinn:correspondence.sblbridge");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsync("storage/api/v1/sblbridge/correspondencerecipient?partyId=0", null);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);           
        }
        
        private HttpClient GetTestClient()
        {
            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IPartiesWithInstancesClient, PartiesWithInstancesClientMock>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                });
            }).CreateClient();

            return client;
        }
    }
}
