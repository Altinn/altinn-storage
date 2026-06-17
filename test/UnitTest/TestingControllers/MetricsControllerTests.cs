using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class MetricsControllerTests(TestApplicationFactory<MetricsController> factory)
    : IClassFixture<TestApplicationFactory<MetricsController>>
{
    private readonly TestApplicationFactory<MetricsController> _factory = factory;

    private const string _basePath = "storage/api/v1/metrics";

    private const string _validApiKey = "test-metrics-api-key";

    [Fact]
    public async Task Get_DailyMetrics_ReturnsOk()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        serviceMock
            .Setup(e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyMetrics<DailyInstanceMetricsRecord>());

        MemoryStream stream = new(Encoding.UTF8.GetBytes("test"));
        serviceMock
            .Setup(e =>
                e.GetParquetFile(
                    It.IsAny<DailyMetrics<DailyInstanceMetricsRecord>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new MetricsSummary
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    FileName = "instancemetrics",
                    FileStream = stream,
                    FileSizeBytes = stream.Length,
                    FileHash = "dummyhash",
                }
            );

        HttpClient client = GetTestClient(serviceMock);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        message.Headers.Add("X-API-Key", _validApiKey);

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("dummyhash", response.Headers.GetValues("X-File-Hash").FirstOrDefault());
        Assert.Equal("4", response.Headers.GetValues("X-File-Size").FirstOrDefault());
        Assert.NotNull(response.Headers.GetValues("X-Generated-At").FirstOrDefault());

        serviceMock.Verify(
            e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()),
            Times.Once
        );
        serviceMock.Verify(
            e =>
                e.GetParquetFile(
                    It.IsAny<DailyMetrics<DailyInstanceMetricsRecord>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Get_DailyMetrics_Returns500InternalServerError()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        serviceMock
            .Setup(e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DataException("some exception"));
        HttpClient client = GetTestClient(serviceMock);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        message.Headers.Add("X-API-Key", _validApiKey);

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "Unable to get daily instance statistics, AppId format is invalid",
            await response.Content.ReadAsStringAsync()
        );
        serviceMock.Verify(
            e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()),
            Times.Once
        );
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_DailyMetrics_CdnFailure_Returns500InternalServerError()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        serviceMock
            .Setup(e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("some exception"));
        HttpClient client = GetTestClient(serviceMock);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        message.Headers.Add("X-API-Key", _validApiKey);

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "Unable to get daily instance statistics, CDN failure",
            await response.Content.ReadAsStringAsync()
        );
        serviceMock.Verify(
            e => e.GetDailyInstanceMetrics(It.IsAny<CancellationToken>()),
            Times.Once
        );
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_DailyMetrics_MissingApiKey_ReturnsUnauthorized()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        HttpClient client = GetTestClient(serviceMock);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_DailyMetrics_InvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        HttpClient client = GetTestClient(serviceMock);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        message.Headers.Add("X-API-Key", "wrong-key");

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_DailyMetrics_ApiKeyNotConfigured_ReturnsUnauthorized()
    {
        // Arrange
        Mock<IMetricsService> serviceMock = new();
        HttpClient client = GetTestClient(serviceMock, configuredApiKey: null);
        const string uri = $"{_basePath}/instances";
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        message.Headers.Add("X-API-Key", _validApiKey);

        // Act
        using HttpResponseMessage response = await client.SendAsync(
            message,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        serviceMock.VerifyNoOtherCalls();
    }

    private HttpClient GetTestClient(
        Mock<IMetricsService>? metricsService = null,
        string? configuredApiKey = _validApiKey
    )
    {
        HttpClient client = _factory
            .WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration(
                    (_, config) =>
                    {
                        config.AddConfiguration(configuration);
                        if (configuredApiKey is not null)
                        {
                            config.AddInMemoryCollection(
                                new Dictionary<string, string?>
                                {
                                    ["MetricsApiKey"] = configuredApiKey,
                                }
                            );
                        }
                    }
                );

                builder.ConfigureTestServices(services =>
                {
                    if (metricsService is not null)
                    {
                        services.AddSingleton(metricsService.Object);
                    }

                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                });
            })
            .CreateClient();

        return client;
    }
}
