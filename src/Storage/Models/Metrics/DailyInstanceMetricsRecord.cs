using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Record describing the storage metrics for an application.
/// </summary>
public record DailyInstanceMetricsRecord
{
    /// <summary>
    /// Gets or sets the number of instances stored.
    /// Aggregated by Day, ResourceId, and ServiceOwnerCode
    /// </summary>
    [JsonPropertyName("instanceCount")]
    public long InstanceCount { get; set; }

    /// <summary>
    /// Gets or sets the service owner code.
    /// </summary>
    [JsonPropertyName("serviceOwnerCode")]
    public string ServiceOwnerCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service owner org number.
    /// </summary>
    [JsonPropertyName("serviceOwnerOrgNumber")]
    public string ServiceOwnerOrgNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource identifier.
    /// </summary>
    [JsonPropertyName("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource title.
    /// </summary>
    [JsonPropertyName("resourceTitle")]
    public string ResourceTitle { get; set; } = string.Empty;
}
