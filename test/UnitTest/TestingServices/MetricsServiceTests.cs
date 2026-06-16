using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class MetricsServiceTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
    };

    [Fact]
    public async Task GetDailyInstanceMetrics()
    {
        // Arrange
        DailyMetrics<DailyInstanceMetricsRecord> metrics = CreateMetrics();
        DailyInstanceMetricsRecord record = metrics.Metrics[0];

        const string orgNr = "991825827";
        Org org = new()
        {
            Orgnr = orgNr,
            Name = new Dictionary<string, string> { { "nb", "Digitaliseringsdirektoratet" } },
        };
        OrgList orgList = new() { Orgs = new Dictionary<string, Org> { { "digdir", org } } };

        string content = JsonSerializer.Serialize(orgList, _jsonOptions);
        HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json
                ),
            }
        );

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(metrics);

        MetricsService service = SetupService(metricsRepositoryMock, null, null, httpClient);

        // Act
        var response = await service.GetDailyInstanceMetrics(CancellationToken.None);

        // Assert
        Assert.Single(response.Metrics);
        Assert.Equal(record.InstanceCount, response.Metrics[0].InstanceCount);
        Assert.Equal(record.ResourceId, response.Metrics[0].ResourceId);
        Assert.Equal(record.ResourceTitle, response.Metrics[0].ResourceTitle);
        Assert.Equal(record.ServiceOwnerCode, response.Metrics[0].ServiceOwnerCode);
        Assert.Equal(orgNr, response.Metrics[0].ServiceOwnerOrgNumber);

        metricsRepositoryMock.Verify(
            e => e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetDailyInstanceMetrics_OrgRequestFails_ThrowsInvalidOperationException()
    {
        // Arrange
        HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }
        );

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateMetrics());

        MetricsService service = SetupService(metricsRepositoryMock, null, null, httpClient);

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.GetDailyInstanceMetrics(CancellationToken.None)
        );

        // Assert
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task GetDailyInstanceMetrics_OrgResponseInvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "not valid json",
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json
                ),
            }
        );

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateMetrics());

        MetricsService service = SetupService(metricsRepositoryMock, null, null, httpClient);

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.GetDailyInstanceMetrics(CancellationToken.None)
        );

        // Assert
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task GetDailyInstanceMetrics_OrgResponseDeserializesToNull_ThrowsInvalidOperationException()
    {
        // Arrange
        HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("null", Encoding.UTF8, MediaTypeNames.Application.Json),
            }
        );

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateMetrics());

        MetricsService service = SetupService(metricsRepositoryMock, null, null, httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetDailyInstanceMetrics(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetParquetFile()
    {
        // Arrange
        DailyMetrics<DailyInstanceMetricsRecord> metrics = CreateMetrics();

        MetricsService service = SetupService();

        // Act
        MetricsSummary response = await service.GetParquetFile(metrics, CancellationToken.None);

        // Assert
        Assert.Equal($"{metrics.DateTime:yyyyMMdd}_instance_storage.parquet", response.FileName);
    }

    private static HttpClient CreateHttpClient(HttpResponseMessage response)
    {
        Mock<HttpMessageHandler> mockHandler = new(behavior: MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        return new HttpClient(mockHandler.Object);
    }

    private static DailyMetrics<DailyInstanceMetricsRecord> CreateMetrics()
    {
        DailyInstanceMetricsRecord record = new()
        {
            InstanceCount = 1,
            ResourceId = "123456",
            ResourceTitle = "Test",
            ServiceOwnerCode = "digdir",
            ServiceOwnerOrgNumber = "0",
        };
        return new DailyMetrics<DailyInstanceMetricsRecord>
        {
            DateTime = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Metrics = [record],
        };
    }

    private static MetricsService SetupService(
        Mock<IMetricsRepository>? repositoryMock = null,
        Mock<ILogger<MetricsService>>? loggerMock = null,
        GeneralSettings? generalSettings = null,
        HttpClient? httpClient = null
    )
    {
        return new MetricsService(
            repositoryMock?.Object ?? new Mock<IMetricsRepository>().Object,
            loggerMock?.Object ?? new Mock<ILogger<MetricsService>>().Object,
            Options.Create(
                generalSettings
                    ?? new GeneralSettings
                    {
                        OrganisationsUrl = "https://altinncdn.no/orgs/altinn-orgs.json",
                    }
            ),
            httpClient ?? new Mock<HttpClient>().Object
        );
    }
}
