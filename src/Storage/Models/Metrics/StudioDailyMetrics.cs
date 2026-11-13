using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// What happened to one service owner's instances on one UTC day, one entry per app, as read by Altinn Studio.
/// </summary>
public record StudioDailyMetrics
{
    /// <summary>
    /// The service owner the metrics belong to.
    /// </summary>
    [JsonPropertyName("org")]
    public required string Org { get; init; }

    /// <summary>
    /// The UTC day the metrics describe.
    /// </summary>
    [JsonPropertyName("date")]
    public required DateOnly Date { get; init; }

    /// <summary>
    /// When Storage computed the metrics.
    /// </summary>
    [JsonPropertyName("computedAt")]
    public required DateTime ComputedAt { get; init; }

    /// <summary>
    /// Metrics per app. Apps without instances created or completed on the day are left out.
    /// </summary>
    [JsonPropertyName("apps")]
    public List<StudioAppDailyMetrics> Apps { get; init; } = [];
}
