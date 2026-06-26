using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Generic container for a day's worth of metrics records of type <typeparamref name="T"/>.
/// </summary>
public record DailyMetrics<T>
{
    /// <summary>
    /// Datetime for when the metrics were based on
    /// </summary>
    [JsonPropertyName("dateTime")]
    public DateTime DateTime { get; init; }

    /// <summary>
    /// A list of metrics
    /// </summary>
    [JsonPropertyName("metrics")]
    public List<T> Metrics { get; init; } = [];
}
