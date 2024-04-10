using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Storage.Configuration;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Moq;
using Moq.Protected;

using Xunit;

namespace Altinn.Platform.Storage.Tests.Extensions
{
    public class HttpClientExtensionTest
    {
        [Fact]
        public async Task PostAsync_ShouldAddAuthorizationHeaderAndReturnHttpResponseMessage()
        {
            // Arrange
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("dummyData", Encoding.UTF8, "application/json")
            };

            Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, _) => SetRequest(request))
                .ReturnsAsync(httpResponseMessage);

            HttpClient httpClient = new HttpClient(handlerMock.Object);
            string requestUri = "http://example.com/api/resource";
            HttpContent content = new StringContent("dummyContent");

            // Act
            var response = await httpClient.PostAsync(requestUri, content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("StringContent", response.Content.GetType().Name);
        }

        [Fact]
        public async Task GetAsync_ShouldAddAuthorizationHeaderAndReturnHttpResponseMessage()
        {
            // Arrange
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("dummyData", Encoding.UTF8, "application/json")
            };

            Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            HttpRequestMessage actualRequest = null;
            void SetRequest(HttpRequestMessage request) => actualRequest = request;
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, _) => SetRequest(request))
                .ReturnsAsync(httpResponseMessage);

            HttpClient httpClient = new HttpClient(handlerMock.Object);
            string requestUri = "http://example.com/api/resource";
            HttpContent content = new StringContent("dummyContent");

            // Act
            var response = await httpClient.GetAsync(requestUri);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("StringContent", response.Content.GetType().Name);
        }
    }
}
