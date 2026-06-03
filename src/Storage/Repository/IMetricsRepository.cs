using System;
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
    /// <param name="dateTime">DateTime</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        DateTime dateTime,
        CancellationToken cancellationToken
    );
}
