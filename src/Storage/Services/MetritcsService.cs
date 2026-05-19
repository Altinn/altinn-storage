using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;
using Parquet.Serialization;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Implementation of <see cref="IMetricsService"/>
/// </summary>
public class MetricsService(IMetricsRepository metricsRepository, ILogger<MetricsService> logger)
    : IMetricsService
{
    private const int _daysOffsetForDailyMetrics = 1; // Fetch yesterdays metrics

    /// <inheritdoc/>
    public async Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        CancellationToken cancellationToken
    )
    {
        DateTime date = DateTime.UtcNow.AddDays(-_daysOffsetForDailyMetrics);

        var metrics = await metricsRepository.GetDailyInstanceMetrics(
            date.Day,
            date.Month,
            date.Year,
            cancellationToken
        );
        var metricsWithOrgNumbers = await AddOrgNumberToMetrics(metrics);
        return metricsWithOrgNumbers;
    }

    /// <inheritdoc/>
    public async Task<MetricsSummary> GetParquetFile<T>(
        DailyMetrics<T> metrics,
        CancellationToken cancellationToken
    )
    {
        (Stream parquetStream, string fileHash, long fileSize) = await GenerateParquetFileStream(
            metrics,
            cancellationToken
        );

        string type = typeof(T).Name switch
        {
            nameof(DailyInstanceMetricsRecord) => "instance",
            _ => throw new InvalidOperationException($"Unsupported metrics type: {typeof(T).Name}"),
        };

        string fileName =
            $"{metrics.Year}{metrics.Month:00}{metrics.Day:00}_{type}_storage.parquet";

        MetricsSummary response = new()
        {
            FileStream = parquetStream,
            FileName = fileName,
            FileHash = fileHash,
            FileSizeBytes = fileSize,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
        return response;
    }

    private async Task<(
        Stream ParquetStream,
        string FileHash,
        long FileSize
    )> GenerateParquetFileStream<T>(DailyMetrics<T> metrics, CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating daily summary parquet file.");

        var parquetData = metrics.Metrics;

        MemoryStream memoryStream = new();

        await ParquetSerializer.SerializeAsync(
            parquetData,
            memoryStream,
            cancellationToken: cancellationToken
        );
        memoryStream.Position = 0;

        string hash = Convert.ToBase64String(
            await MD5.HashDataAsync(memoryStream, cancellationToken)
        );
        memoryStream.Position = 0;

        logger.LogInformation("Successfully generated daily summary parquet file stream");

        return (memoryStream, hash, memoryStream.Length);
    }

    private static async Task<DailyMetrics<DailyInstanceMetricsRecord>> AddOrgNumberToMetrics(
        DailyMetrics<DailyInstanceMetricsRecord> metrics
    )
    {
        // TODO Get data from CDN, map all orgCodes to orgNumber
        await Task.Delay(1000);
        foreach (DailyInstanceMetricsRecord record in metrics.Metrics)
        {
            record.ServiceOwnerOrgNumber = 0;
        }
        return metrics;
    }
}
