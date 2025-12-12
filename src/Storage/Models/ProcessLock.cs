using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Represents a process lock on an instance
/// </summary>
public sealed class ProcessLock
{
    /// <summary>
    /// Gets or sets the unique identifier for the lock
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the internal ID of the instance that is locked
    /// </summary>
    public long InstanceInternalId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the lock was acquired
    /// </summary>
    public DateTimeOffset LockedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the lock expires
    /// </summary>
    public DateTimeOffset LockedUntil { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user or process that acquired the lock
    /// </summary>
    public required string LockedBy { get; set; }
}
