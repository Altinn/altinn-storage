using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Number of open instances standing at one process task.
/// </summary>
public record StudioOpenTaskCount
{
    /// <summary>
    /// The process task id, for example Task_1.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; init; }

    /// <summary>
    /// Open instances at the task.
    /// </summary>
    [JsonPropertyName("openInstances")]
    public long OpenInstances { get; init; }
}
