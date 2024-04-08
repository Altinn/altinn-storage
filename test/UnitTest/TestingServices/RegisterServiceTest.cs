using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Register.Enums;
using Altinn.Platform.Register.Models;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Exceptions;
using Altinn.Platform.Storage.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;
using Moq.Protected;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class RegisterServiceTest
    {
        private readonly Mock<IOptions<PlatformSettings>> _platformSettings;
        private readonly Mock<IOptions<GeneralSettings>> _generalSettings;
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly Mock<IHttpContextAccessor> _contextAccessor;
        private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator;
        private readonly Mock<ILogger<RegisterService>> _loggerRegisterService;

        public RegisterServiceTest()
        {
            _platformSettings = new Mock<IOptions<PlatformSettings>>();
            _generalSettings = new Mock<IOptions<GeneralSettings>>();
            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _contextAccessor = new Mock<IHttpContextAccessor>();
            _accessTokenGenerator = new Mock<IAccessTokenGenerator>();
            _loggerRegisterService = new Mock<ILogger<RegisterService>>();
        }

        [Fact]
        public async Task PartyLookup_MatchFound_IdReturned()
        {
            // Arrange
            Party party = new Party
            {
                PartyId = 500000,
                OrgNumber = "897069650",
                PartyTypeName = PartyType.Organisation
            };
            int expected = 500000;
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(party), Encoding.UTF8, "application/json")
            };

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            InitializeMocks(httpResponseMessage, SetRequest);

            HttpClient httpClient = new HttpClient(_handlerMock.Object);

            RegisterService target = new RegisterService(
                httpClient,
                _contextAccessor.Object,
                _accessTokenGenerator.Object,
                _generalSettings.Object,
                _platformSettings.Object,
                new Mock<ILogger<RegisterService>>().Object);

            // Act
            int actual = await target.PartyLookup("897069650", null);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task GetParty_SuccessResponse_PartyTypeDeserializedSuccessfully()
        {
            // Arrange         
            PartyType expectedPartyType = PartyType.Organisation;

            string repsonseString = "{\"partyId\": 500000," +
                "\"partyTypeName\": \"Organisation\"," +
                "\"orgNumber\": \"897069650\"," +
                "\"unitType\": \"AS\"," +
                "\"name\": \"DDG Fitness\"," +
                "\"isDeleted\": false," +
                "\"onlyHierarchyElementWithNoAccess\": false," +
                "\"organization\": {\"orgNumber\": \"897069650\",\"name\": \"DDG Fitness\",\"unitType\": \"AS\",\"telephoneNumber\": \"12345678\",\"mobileNumber\": \"92010000\",\"faxNumber\": \"92110000\",\"eMailAddress\": \"central@ddgfitness.no\",\"internetAddress\": \"http://ddgfitness.no\",\"mailingAddress\": \"Sofies Gate 1\",\"mailingPostalCode\": \"0170\",\"mailingPostalCity\": \"Oslo\",\"businessAddress\": \"Sofies Gate 1\",\"businessPostalCode\": \"0170\",\"businessPostalCity\": \"By\",\"unitStatus\": null},\"childParties\": null\r\n}";

            HttpResponseMessage httpResponseMessage = new()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(repsonseString, Encoding.UTF8, "application/json")
            };

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            InitializeMocks(httpResponseMessage, SetRequest);

            HttpClient httpClient = new HttpClient(_handlerMock.Object);

            RegisterService target = new RegisterService(
                httpClient,
                _contextAccessor.Object,
                _accessTokenGenerator.Object,
                _generalSettings.Object,
                _platformSettings.Object,
                new Mock<ILogger<RegisterService>>().Object);

            // Act
            Party actual = await target.GetParty(500000);

            // Assert
            Assert.Equal(expectedPartyType, actual.PartyTypeName);
        }

        [Fact]
        public async Task GetParty_BadRequestResponse_PartyTypeDeserializationFailedWithHttpStatusCode()
        {
            // Arrange
            string repsonseString = "{\"partyId\": 500000," +
                "\"partyTypeName\": \"Organisation\"," +
                "\"orgNumber\": \"897069650\"," +
                "\"unitType\": \"AS\"," +
                "\"name\": \"DDG Fitness\"," +
                "\"isDeleted\": false," +
                "\"onlyHierarchyElementWithNoAccess\": false," +
                "\"organization\": {\"orgNumber\": \"897069650\",\"name\": \"DDG Fitness\",\"unitType\": \"AS\",\"telephoneNumber\": \"12345678\",\"mobileNumber\": \"92010000\",\"faxNumber\": \"92110000\",\"eMailAddress\": \"central@ddgfitness.no\",\"internetAddress\": \"http://ddgfitness.no\",\"mailingAddress\": \"Sofies Gate 1\",\"mailingPostalCode\": \"0170\",\"mailingPostalCity\": \"Oslo\",\"businessAddress\": \"Sofies Gate 1\",\"businessPostalCode\": \"0170\",\"businessPostalCity\": \"By\",\"unitStatus\": null},\"childParties\": null\r\n}";

            HttpResponseMessage httpResponseMessage = new()
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(repsonseString, Encoding.UTF8, "application/json")
            };

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            InitializeMocks(httpResponseMessage, SetRequest);

            HttpClient httpClient = new HttpClient(_handlerMock.Object);

            RegisterService target = new RegisterService(
                httpClient,
                _contextAccessor.Object,
                _accessTokenGenerator.Object,
                _generalSettings.Object,
                _platformSettings.Object,
                _loggerRegisterService.Object);

            // Act
            Party actual = await target.GetParty(500000);

            // Assert
            _loggerRegisterService.Verify(
                x => x.LogError(
                    It.Is<string>(s => s == repsonseString),
                    "500000",
                    HttpStatusCode.BadRequest),
                Times.Once);
        }

        [Fact]
        public async Task PartyLookup_ResponseIsNotSuccessful_PlatformExceptionThrown()
        {
            // Arrange
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent(string.Empty)
            };

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            InitializeMocks(httpResponseMessage, SetRequest);

            HttpClient httpClient = new HttpClient(_handlerMock.Object);

            RegisterService target = new RegisterService(
                httpClient,
                _contextAccessor.Object,
                _accessTokenGenerator.Object,
                _generalSettings.Object,
                _platformSettings.Object,
                new Mock<ILogger<RegisterService>>().Object);

            // Act & Assert
            await Assert.ThrowsAsync<PlatformHttpException>(async () => { await target.PartyLookup("16069412345", null); });
        }

        private void InitializeMocks(HttpResponseMessage httpResponseMessage, Action<HttpRequestMessage> callback)
        {
            PlatformSettings platformSettings = new PlatformSettings
            {
                ApiRegisterEndpoint = "http://localhost:5101/register/api/v1/"
            };

            _platformSettings.Setup(s => s.Value).Returns(platformSettings);

            GeneralSettings generalSettings = new GeneralSettings
            {
                JwtCookieName = "AltinnStudioRuntime"
            };

            _generalSettings.Setup(s => s.Value).Returns(generalSettings);

            _contextAccessor.Setup(s => s.HttpContext).Returns(new DefaultHttpContext());

            _accessTokenGenerator.Setup(s => s.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(string.Empty);

            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, _) => callback(request))
                .ReturnsAsync(httpResponseMessage)
                .Verifiable();
        }
    }
}
