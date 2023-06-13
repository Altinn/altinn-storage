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
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.Tests.Mocks;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
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

        private readonly TestApplicationFactory<InstancesController> _factory;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="factory">The web application factory.</param>
        public SignControllerTests(TestApplicationFactory<InstancesController> factory)
        {
            _factory = factory;
        }

        [Fact]  
        public async Task SignRequest_Ok()
        {
            // Arrange
            int instanceOwnerPartyId = 1337;
            string instanceGuid = "46133fb5-a9f2-45d4-90b1-f6d93ad40713";
            string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

            HttpClient client = GetTestClient();
            string token = PrincipalUtil.GetToken(1337, 1606, 3);
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
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private HttpClient GetTestClient(Mock<IInstanceService> instanceServiceMock = null)
        {
            // No setup required for these services. They are not in use by the InstanceController
            Mock<ISasTokenProvider> sasTokenProvider = new Mock<ISasTokenProvider>();
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();

            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    if (instanceServiceMock != null)
                    {
                        services.AddSingleton(instanceServiceMock.Object);
                    }

                    services.AddSingleton(sasTokenProvider.Object);
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