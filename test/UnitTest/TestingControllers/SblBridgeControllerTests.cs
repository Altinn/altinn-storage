using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        public async Task RegisterAltinn3CorrespondenceRecipient_InvalidPartyId_ReturnsBadRequestAsync()
        {
            var client = GetTestClient();
            var response = await client.PostAsync("correspondencerecipient?partyId=123", null);
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
                });
            }).CreateClient();

            return client;
        }
    }
}
