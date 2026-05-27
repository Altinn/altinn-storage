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
    public long InstanceCount { get; set; }

    /// <summary>
    /// Gets or sets the service owner code.
    /// </summary>
    public string ServiceOwnerCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service owner org number.
    /// </summary>
    public int? ServiceOwnerOrgNumber { get; set; }

    /// <summary>
    /// Gets or sets the resource identifier.
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource title.
    /// </summary>
    public string ResourceTitle { get; set; } = string.Empty;
}
