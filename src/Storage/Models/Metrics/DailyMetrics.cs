using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Generic container for a day's worth of metrics records of type <typeparamref name="T"/>.
/// </summary>
public record DailyMetrics<T>
{
    /// <summary>
    /// The day of the month the metrics apply for
    /// </summary>
    public int Day { get; init; }

    /// <summary>
    /// The month the metrics apply for
    /// </summary>
    public int Month { get; init; }

    /// <summary>
    /// The year the metrics apply for
    /// </summary>
    public int Year { get; init; }

    /// <summary>
    /// A list of metrics
    /// </summary>
    public List<T> Metrics { get; init; } = [];
}
