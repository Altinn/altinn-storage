using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Instance metrics for one app on one UTC day.
/// </summary>
public record StudioAppDailyMetrics
{
    /// <summary>
    /// The app name without the service owner prefix.
    /// </summary>
    [JsonPropertyName("app")]
    public required string App { get; init; }

    /// <summary>
    /// Instances created on the day.
    /// </summary>
    [JsonPropertyName("instancesCreated")]
    public long InstancesCreated { get; init; }

    /// <summary>
    /// Instances archived on the day.
    /// </summary>
    [JsonPropertyName("instancesCompleted")]
    public long InstancesCompleted { get; init; }

    /// <summary>
    /// Median seconds from process start to process end for the instances archived on the day. Null when none were archived.
    /// </summary>
    [JsonPropertyName("secondsToCompleteP50")]
    public int? SecondsToCompleteP50 { get; init; }
}
