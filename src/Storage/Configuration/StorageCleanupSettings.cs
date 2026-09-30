#nullable disable

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Configuration for storage cleanup tasks.
/// </summary>
public class StorageCleanupSettings
{
    /// <summary>
    /// Minimum retention for aggregate mutation idempotency records. The workflow engine retry window
    /// defaults to 24 hours, so cleanup must keep a margin above that.
    /// </summary>
    public const int MinimumInstanceMutationIdempotencyRetentionHours = 48;

    /// <summary>
    /// Gets or sets how long aggregate mutation idempotency records are retained before cleanup.
    /// Values below <see cref="MinimumInstanceMutationIdempotencyRetentionHours"/> are clamped.
    /// </summary>
    /// <remarks>
    /// Workflow retries may also need the original blob versions. storage.readorphanblobversionsforcleanup
    /// retains detached versions for a set period. Both retention periods must outlast the workflow retry
    /// window.
    /// </remarks>
    public int InstanceMutationIdempotencyRetentionHours { get; set; } =
        MinimumInstanceMutationIdempotencyRetentionHours;
}
