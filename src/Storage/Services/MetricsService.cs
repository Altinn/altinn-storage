using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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
public class MetricsService : IMetricsService
{
    private readonly IMetricsRepository _metricsRepository;
    private readonly ILogger<MetricsService> _logger;
    private readonly IOrganisationService _organisationService;

    private const int _daysOffsetForDailyMetrics = 1; // Fetch yesterday's metrics

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsService"/> class.
    /// </summary>
    /// <param name="metricsRepository">Metrics repository</param>
    /// <param name="logger">Logger</param>
    /// <param name="organisationService">Organisation service used to translate service owner codes to org numbers</param>
    public MetricsService(
        IMetricsRepository metricsRepository,
        ILogger<MetricsService> logger,
        IOrganisationService organisationService
    )
    {
        _metricsRepository = metricsRepository;
        _logger = logger;
        _organisationService = organisationService;
    }

    /// <inheritdoc/>
    public async Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        CancellationToken cancellationToken
    )
    {
        DateTime date = DateTime.UtcNow.AddDays(-_daysOffsetForDailyMetrics);

        var metrics = await _metricsRepository.GetDailyInstanceMetrics(date, cancellationToken);
        try
        {
            return await AddOrgNumberToMetrics(metrics, cancellationToken);
        }
        catch (Exception e)
            when (e is InvalidOperationException or HttpRequestException or JsonException)
        {
            _logger.LogError(
                e,
                "Failed to enrich daily instance metrics for {date:yyyy-MM-dd}",
                date
            );
            throw new InvalidOperationException(
                $"Failed to enrich daily instance metrics for {date:yyyy-MM-dd} with organisation numbers.",
                e
            );
        }
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

        string fileName = $"{metrics.DateTime:yyyyMMdd}_{type}_storage.parquet";

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

    private static async Task<(
        Stream ParquetStream,
        string FileHash,
        long FileSize
    )> GenerateParquetFileStream<T>(DailyMetrics<T> metrics, CancellationToken cancellationToken)
    {
        var parquetData = metrics.Metrics;

        MemoryStream memoryStream = new();

        await ParquetSerializer.SerializeAsync(
            parquetData,
            memoryStream,
            cancellationToken: cancellationToken
        );
        memoryStream.Position = 0;

#pragma warning disable S4790 // MD5 is intentionally used here as a non-cryptographic content checksum (deduplication/transport integrity only) — not in any security-sensitive context.
        string hash = Convert.ToBase64String(
            await MD5.HashDataAsync(memoryStream, cancellationToken)
        );
#pragma warning restore S4790
        memoryStream.Position = 0;

        return (memoryStream, hash, memoryStream.Length);
    }

    private async Task<DailyMetrics<DailyInstanceMetricsRecord>> AddOrgNumberToMetrics(
        DailyMetrics<DailyInstanceMetricsRecord> metrics,
        CancellationToken cancellationToken = default
    )
    {
        foreach (DailyInstanceMetricsRecord record in metrics.Metrics)
        {
            string? orgNumber = await _organisationService.GetOrgNumber(
                record.ServiceOwnerCode,
                cancellationToken
            );

            if (orgNumber is not null)
            {
                record.ServiceOwnerOrgNumber = orgNumber;
            }
        }

        return metrics;
    }
}
