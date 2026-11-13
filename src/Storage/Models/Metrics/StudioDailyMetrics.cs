using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Instance metrics for one service owner and one UTC day, one entry per app, as read by Altinn Studio.
/// </summary>
public record StudioDailyMetrics
{
    /// <summary>
    /// The service owner the metrics belong to.
    /// </summary>
    [JsonPropertyName("org")]
    public required string Org { get; init; }

    /// <summary>
    /// The UTC day the flow metrics (created, completed, time to complete) describe.
    /// </summary>
    [JsonPropertyName("date")]
    public required DateOnly Date { get; init; }

    /// <summary>
    /// When the snapshot metrics (open instances) were read.
    /// </summary>
    [JsonPropertyName("computedAt")]
    public required DateTime ComputedAt { get; init; }

    /// <summary>
    /// Metrics per app. Apps without instances on the day and without open instances are left out.
    /// </summary>
    [JsonPropertyName("apps")]
    public List<StudioAppDailyMetrics> Apps { get; init; } = [];
}
