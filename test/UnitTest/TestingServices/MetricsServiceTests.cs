using System;
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
        Mock<IMetricsRepository> metricsRepositoryMock = new();
        DailyInstanceMetricsRecord record = new()
        {
            InstanceCount = 1,
            ResourceId = "123456",
            ResourceTitle = "Test",
            ServiceOwnerCode = "ttd",
            ServiceOwnerOrgNumber = 0,
        };
        DailyMetrics<DailyInstanceMetricsRecord> metrics = new()
        {
            Day = 1,
            Month = 1,
            Year = 2022,
            Metrics = [record],
        };

        metricsRepositoryMock
            .Setup(e =>
                e.GetDailyInstanceMetrics(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(metrics);

        MetricsService service = SetupService(metricsRepositoryMock);

        // Act
        var response = await service.GetDailyInstanceMetrics(CancellationToken.None);

        // Assert
        Assert.Single(response.Metrics);
        Assert.Equal(record.InstanceCount, response.Metrics[0].InstanceCount);
        Assert.Equal(record.ResourceId, response.Metrics[0].ResourceId);
        Assert.Equal(record.ResourceTitle, response.Metrics[0].ResourceTitle);
        Assert.Equal(record.ServiceOwnerCode, response.Metrics[0].ServiceOwnerCode);
        Assert.Equal(record.ServiceOwnerOrgNumber, response.Metrics[0].ServiceOwnerOrgNumber);

        metricsRepositoryMock.Verify(
            e =>
                e.GetDailyInstanceMetrics(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetParquetFile()
    {
        // Arrange
        DailyInstanceMetricsRecord record = new()
        {
            InstanceCount = 1,
            ResourceId = "123456",
            ResourceTitle = "Test",
            ServiceOwnerCode = "ttd",
            ServiceOwnerOrgNumber = 0,
        };
        DailyMetrics<DailyInstanceMetricsRecord> metrics = new()
        {
            Day = 1,
            Month = 1,
            Year = 2022,
            Metrics = [record],
        };

        MetricsService service = SetupService();

        // Act
        MetricsSummary response = await service.GetParquetFile(metrics, CancellationToken.None);

        // Assert
        Assert.Equal(
            $"{metrics.Year}{metrics.Month:00}{metrics.Day:00}_instance_storage.parquet",
            response.FileName
        );
    }

    private MetricsService SetupService(
        Mock<IMetricsRepository>? repositoryMock = null,
        Mock<ILogger<MetricsService>>? loggerMock = null
    )
    {
        return new MetricsService(
            repositoryMock?.Object ?? new Mock<IMetricsRepository>().Object,
            loggerMock?.Object ?? new Mock<ILogger<MetricsService>>().Object
        );
    }
}
