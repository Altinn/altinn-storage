using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Model for metrics for daily statistics for either email or sms
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
    /// A list of metrics for each individual notification
    /// </summary>
    public List<T> Metrics { get; init; } = [];
}
