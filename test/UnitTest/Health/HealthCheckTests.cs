using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Health;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Health
{
    public class HealthCheckTests : IClassFixture<TestApplicationFactory<HealthCheck>>
    {
        private readonly TestApplicationFactory<HealthCheck> _factory;

        public HealthCheckTests(TestApplicationFactory<HealthCheck> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Verify that component responds on health check
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task VerifyHeltCheck_OK()
        {
            HttpClient client = GetTestClient();

            HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "/health")
            {
            };

            HttpResponseMessage response = await client.SendAsync(httpRequestMessage);
            await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private HttpClient GetTestClient()
        {
            Mock<IMessageBus> busMock = new Mock<IMessageBus>();
            
            Environment.SetEnvironmentVariable("WolverineSettings__ServiceBusConnectionString", string.Empty);
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
                    services.AddSingleton(busMock.Object);
                });
            }).CreateClient();

            return client;
        }
    }
}
