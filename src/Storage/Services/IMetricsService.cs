using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Interface for metrics service
/// </summary>
public interface IMetricsService
{
    /// <summary>
    /// Get daily metrics for instances
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Creates a Parquet file stream from the provided daily metrics.
    /// </summary>
    /// <param name="metrics">The daily metrics to create the Parquet file from.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<MetricsSummary> GetParquetFile<T>(
        DailyMetrics<T> metrics,
        CancellationToken cancellationToken
    );
}
