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
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;

using AltinnCore.Authentication.JwtCookie;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

using static Altinn.Platform.Storage.Interface.Models.SignRequest;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class SignControllerTests : IClassFixture<TestApplicationFactory<SignController>>
    {
        private const string BasePath = "storage/api/v1/instances";

        private readonly TestApplicationFactory<SignController> _factory;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="factory">The web application factory.</param>
        public SignControllerTests(TestApplicationFactory<SignController> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SignRequest_UserHasRequiredRole_Created()
        {
            // Arrange
            int instanceOwnerPartyId = 1600;
            string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
            string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

            Mock<IInstanceService> instanceServiceMock = new Mock<IInstanceService>();
            instanceServiceMock.Setup(ism => 
            ism.CreateSignDocument(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<SignRequest>(), It.IsAny<string>()))
            .ReturnsAsync((true, null));

            HttpClient client = GetTestClient(instanceServiceMock);
            string token = PrincipalUtil.GetToken(10016, 1600, 2);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);    

            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" }
            };

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task SignRequest_UserDoesNotHaveRequiredRole_Forbidden()
        {
            // Arrange
            int instanceOwnerPartyId = 1600;
            string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
            string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(43, 12800, 2);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            SignRequest signRequest = new SignRequest();

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task SignRequest_UserHasRequiredRole_InvalidUserId_BadRequest()
        {
            // Arrange
            int instanceOwnerPartyId = 1600;
            string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
            string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(10016, 1600, 2);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);    

            SignRequest signRequest = new SignRequest();

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SignRequest_UserHasRequiredRole_InstanceServiceFail_NotFound()
        {
            // Arrange
            int instanceOwnerPartyId = 1600;
            string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
            string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

            Mock<IInstanceService> instanceServiceMock = new Mock<IInstanceService>();
            instanceServiceMock.Setup(ism => 
            ism.CreateSignDocument(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<SignRequest>(), It.IsAny<string>()))
            .ReturnsAsync((false, new ServiceError(404, "Instance not found")));
            
            HttpClient client = GetTestClient(instanceServiceMock);
            string token = PrincipalUtil.GetToken(10016, 1600, 2);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);    

            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" }
            };

            // Act
            HttpResponseMessage response = await client.PostAsync(requestUri, JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json")));

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        private HttpClient GetTestClient(Mock<IInstanceService> instanceServiceMock = null)
        {
            // No setup required for these services. They are not in use by the InstanceController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();

            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    if (instanceServiceMock != null)
                    {
                        services.AddSingleton(instanceServiceMock.Object);
                    }

                    services.AddMockRepositories();
                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton<IPartiesWithInstancesClient, PartiesWithInstancesClientMock>();
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                });
            }).CreateClient();

            return client;
        }
    }
}
