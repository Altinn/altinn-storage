using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Tests.Stubs;
using Xunit;

namespace Altinn.Platform.Storage.Tests.Extensions;

public class HttpClientExtensionTests
{
    private readonly HttpClient _httpClient;
    private HttpRequestMessage _httpRequest;

    public HttpClientExtensionTests()
    {
        var httpMessageHandler = new DelegatingHandlerStub(
            async (request, token) =>
            {
                _httpRequest = request;
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        );

        _httpClient = new HttpClient(httpMessageHandler);
        _httpClient.BaseAddress = new Uri("http://localhost:5101/register/api/v1/");
    }

    [Fact]
    public async Task PostAsync_ShouldAddAuthorizationHeaderAndReturnHttpResponseMessage()
    {
        // Arrange
        HttpContent content = new StringContent("dummyContent");

        // Act
        _ = await _httpClient.PostAsync(
            "dummyAuthorizationToken",
            "/api/resource",
            content,
            "dummyPlatformAccessToken"
        );

        // Assert
        Assert.True(_httpRequest.Headers.Contains("PlatformAccessToken"));
    }

    [Fact]
    public async Task GetAsync_ShouldAddAuthorizationHeaderAndReturnHttpResponseMessage()
    {
        // Act
        _ = await _httpClient.GetAsync(
            "dummyAuthorizationToken",
            "/api/resource",
            "dummyPlatformAccessToken"
        );

        // Assert
        Assert.True(_httpRequest.Headers.Contains("PlatformAccessToken"));
    }
}
