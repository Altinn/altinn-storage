using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class StudioInstancesControllerTests : IClassFixture<TestApplicationFactory<StudioInstancesController>>
    {
        private readonly TestApplicationFactory<StudioInstancesController> _factory;
        private const string BasePath = "/storage/api/v1/studio/instances";

        public StudioInstancesControllerTests(TestApplicationFactory<StudioInstancesController> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetInstances_ProductionEnvironment_ReturnsNotFound()
        {
            // Arrange
            var hostEnvironmentMock = new Mock<IHostEnvironment>();
            hostEnvironmentMock.Setup(h => h.EnvironmentName).Returns(Environments.Production);
            hostEnvironmentMock.Setup(h => h.ContentRootPath).Returns(Directory.GetCurrentDirectory());

            HttpClient client = GetTestClient(hostEnvironment: hostEnvironmentMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_NoOrgClaim_ReturnsForbid()
        {
            // Arrange
            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(1337, 1337); // Token without org claim
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_MissingScope_ReturnsForbid()
        {
            // Arrange
            var authorizationMock = new Mock<IAuthorization>();
            authorizationMock.Setup(a => a.UserHasRequiredScope(It.IsAny<string>())).Returns(false);

            HttpClient client = GetTestClient(authorizationService: authorizationMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd"); // No scope in token
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_AccessToOtherOrg_ReturnsForbid()
        {
            // Arrange
            var authorizationMock = new Mock<IAuthorization>();
            authorizationMock.Setup(a => a.UserHasRequiredScope(It.IsAny<string>())).Returns(true);

            HttpClient client = GetTestClient(authorizationService: authorizationMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/skd/app");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_AccessingOwnOrg_ReturnsOk()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<InstanceQueryParameters>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InstanceQueryResponse
                {
                    Instances = new List<Instance>
                    {
                        new Instance
                        {
                            Id = "1337/guid",
                            InstanceOwner = new() { PartyId = "1337" },
                            AppId = "ttd/app",
                            Org = "ttd",
                        },
                    }
                });

            HttpClient client = GetTestClient(instanceRepository: instanceRepositoryMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            var queryResponse = JsonConvert.DeserializeObject<QueryResponse<SimpleInstance>>(content);
            Assert.Single(queryResponse.Instances);
        }

        [Fact]
        public async Task GetInstances_RepositoryReturnsException_Returns500()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<InstanceQueryParameters>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InstanceQueryResponse { Exception = "Something went wrong" });

            HttpClient client = GetTestClient(instanceRepository: instanceRepositoryMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_RepositoryThrowsException_Returns500()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<InstanceQueryParameters>(), false, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Database connection error"));

            HttpClient client = GetTestClient(instanceRepository: instanceRepositoryMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task GetInstances_WithContinuationToken_ReturnsOk()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock
                .Setup(ir => ir.GetInstancesFromQuery(It.IsAny<InstanceQueryParameters>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InstanceQueryResponse
                {
                    Instances = new List<Instance>(),
                    ContinuationToken = "nextToken"
                });

            HttpClient client = GetTestClient(instanceRepository: instanceRepositoryMock.Object);
            string token = PrincipalUtil.GetOrgToken("ttd", scope: "altinn:serviceowner/instances.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act
            HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app?continuationToken=someToken");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            var queryResponse = JsonConvert.DeserializeObject<QueryResponse<SimpleInstance>>(content);
            Assert.Equal(System.Web.HttpUtility.UrlEncode("nextToken"), queryResponse.Next);
        }

        private HttpClient GetTestClient(
            IInstanceRepository instanceRepository = null,
            IAuthorization authorizationService = null,
            IHostEnvironment hostEnvironment = null,
            IOptions<GeneralSettings> generalSettings = null)
        {
            if (instanceRepository == null)
            {
                instanceRepository = new Mock<IInstanceRepository>().Object;
            }

            if (authorizationService == null)
            {
                var authorizationMock = new Mock<IAuthorization>();
                authorizationMock.Setup(a => a.UserHasRequiredScope(It.IsAny<List<string>>())).Returns(true);
                authorizationService = authorizationMock.Object;
            }

            if (hostEnvironment == null)
            {
                var hostEnvironmentMock = new Mock<IHostEnvironment>();
                hostEnvironmentMock.Setup(h => h.EnvironmentName).Returns(Environments.Development);
                hostEnvironmentMock.Setup(h => h.ContentRootPath).Returns(Directory.GetCurrentDirectory());
                hostEnvironment = hostEnvironmentMock.Object;
            }

            if (generalSettings == null)
            {
                generalSettings = Options.Create(new GeneralSettings { InstanceReadScope = new List<string>{"altinn:serviceowner/instances.read"} });
            }

            var client = _factory.WithWebHostBuilder(builder =>
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
                    services.AddSingleton(instanceRepository);
                    services.AddSingleton(authorizationService);
                    services.AddSingleton(hostEnvironment);
                    services.AddSingleton(generalSettings);
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                });
            }).CreateClient();

            return client;
        }
    }
}
