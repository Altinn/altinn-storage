using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class MetricsServiceTests
{
    [Fact]
    public async Task GetDailyInstanceMetrics()
    {
        // Arrange
        DailyMetrics<DailyInstanceMetricsRecord> metrics = CreateMetrics();
        DailyInstanceMetricsRecord record = metrics.Metrics[0];

        const string orgNr = "991825827";

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(metrics);

        Mock<IOrganisationService> organisationServiceMock = new();
        organisationServiceMock
            .Setup(e => e.GetOrgNumber("digdir", It.IsAny<CancellationToken>()))
            .ReturnsAsync(orgNr);

        MetricsService service = SetupService(metricsRepositoryMock, null, organisationServiceMock);

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
    public async Task GetDailyInstanceMetrics_OrgLookupFails_ThrowsInvalidOperationException()
    {
        // Arrange
        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateMetrics());

        Mock<IOrganisationService> organisationServiceMock = new();
        organisationServiceMock
            .Setup(e => e.GetOrgNumber(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("CDN unavailable"));

        MetricsService service = SetupService(metricsRepositoryMock, null, organisationServiceMock);

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.GetDailyInstanceMetrics(CancellationToken.None)
        );

        // Assert
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task GetDailyInstanceMetrics_OrgNotFound_LeavesOrgNumberUnchanged()
    {
        // Arrange
        DailyMetrics<DailyInstanceMetricsRecord> metrics = CreateMetrics();
        string originalOrgNumber = metrics.Metrics[0].ServiceOwnerOrgNumber;

        Mock<IMetricsRepository> metricsRepositoryMock = new();
        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(metrics);

        Mock<IOrganisationService> organisationServiceMock = new();
        organisationServiceMock
            .Setup(e => e.GetOrgNumber(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        MetricsService service = SetupService(metricsRepositoryMock, null, organisationServiceMock);

        // Act
        var response = await service.GetDailyInstanceMetrics(CancellationToken.None);

        // Assert
        Assert.Equal(originalOrgNumber, response.Metrics[0].ServiceOwnerOrgNumber);
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
        Mock<IOrganisationService>? organisationServiceMock = null
    )
    {
        return new MetricsService(
            repositoryMock?.Object ?? new Mock<IMetricsRepository>().Object,
            loggerMock?.Object ?? new Mock<ILogger<MetricsService>>().Object,
            organisationServiceMock?.Object ?? new Mock<IOrganisationService>().Object
        );
    }
}
