using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Interface for metrics repository
/// </summary>
public interface IMetricsRepository
{
    /// <summary>
    /// Get daily instance metrics
    /// </summary>
    /// <param name="day">Day</param>
    /// <param name="month">Month</param>
    /// <param name="year">Year</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        int day,
        int month,
        int year,
        CancellationToken cancellationToken
    );
}
