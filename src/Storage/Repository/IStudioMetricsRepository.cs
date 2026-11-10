using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Reads the per-app instance metrics that Altinn Studio shows service owners.
/// </summary>
public interface IStudioMetricsRepository
{
    /// <summary>
    /// Gets the metrics for every app of a service owner for one UTC day.
    /// </summary>
    /// <param name="org">The service owner.</param>
    /// <param name="date">The UTC day.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StudioDailyMetrics> GetDailyMetrics(
        string org,
        DateOnly date,
        CancellationToken cancellationToken
    );
}
