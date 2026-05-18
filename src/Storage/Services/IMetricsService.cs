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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Creates a Parquet file stream from the provided daily metrics.
    /// </summary>
    Task<MetricsSummary> GetParquetFile<T>(
        DailyMetrics<T> metrics,
        CancellationToken cancellationToken
    );
}
