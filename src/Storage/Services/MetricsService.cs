using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet.Serialization;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Implementation of <see cref="IMetricsService"/>
/// </summary>
public class MetricsService : IMetricsService
{
    private readonly IMetricsRepository _metricsRepository;
    private readonly ILogger<MetricsService> _logger;
    private readonly GeneralSettings _generalGeneralSettings;
    private readonly HttpClient _httpClient;

    private const int _daysOffsetForDailyMetrics = 1; // Fetch yesterday's metrics
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsService"/> class.
    /// </summary>
    /// <param name="metricsRepository">Metrics repository</param>
    /// <param name="logger">Logger</param>
    /// <param name="generalSettings">GeneralSettings</param>
    /// <param name="httpClient">HttpClient</param>
    public MetricsService(
        IMetricsRepository metricsRepository,
        ILogger<MetricsService> logger,
        IOptions<GeneralSettings> generalSettings,
        HttpClient httpClient
    )
    {
        _metricsRepository = metricsRepository;
        _logger = logger;
        _generalGeneralSettings = generalSettings.Value;
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        CancellationToken cancellationToken
    )
    {
        DateTime date = DateTime.UtcNow.AddDays(-_daysOffsetForDailyMetrics);

        var metrics = await _metricsRepository.GetDailyInstanceMetrics(
            date.Day,
            date.Month,
            date.Year,
            cancellationToken
        );
        try
        {
            return await AddOrgNumberToMetrics(metrics, cancellationToken);
        }
        catch (Exception e)
            when (e is InvalidOperationException or HttpRequestException or JsonException)
        {
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
        _logger.LogInformation("Generating daily summary parquet file.");

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

        _logger.LogInformation("Successfully generated daily summary parquet file stream");

        return (memoryStream, hash, memoryStream.Length);
    }

    private async Task<DailyMetrics<DailyInstanceMetricsRecord>> AddOrgNumberToMetrics(
        DailyMetrics<DailyInstanceMetricsRecord> metrics,
        CancellationToken cancellationToken = default
    )
    {
        using HttpRequestMessage requestMessage = new(
            HttpMethod.Get,
            _generalGeneralSettings.OrganisationsUrl
        );
        using HttpResponseMessage response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        OrgList? orgList = await JsonSerializer.DeserializeAsync<OrgList?>(
            stream,
            _serializerOptions,
            cancellationToken
        );
        if (orgList is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {nameof(OrgList)}");
        }

        var organisations = orgList.Orgs;
        if (organisations is null)
            throw new InvalidOperationException($"Failed to deserialize {nameof(orgList.Orgs)}");

        foreach (DailyInstanceMetricsRecord record in metrics.Metrics)
        {
            if (!organisations.TryGetValue(record.ServiceOwnerCode, out Org? org))
                continue;

            if (org.Orgnr is not null)
            {
                record.ServiceOwnerOrgNumber = org.Orgnr;
            }
        }
        return metrics;
    }
}
