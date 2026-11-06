using System;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Status fields to merge, with null booleans meaning unchanged.
/// </summary>
public sealed record InstanceMutationStatusChanges
{
    /// <summary>
    /// Gets the archived flag change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsArchived { get; init; }

    /// <summary>
    /// Gets the archived time change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> Archived { get; init; }

    /// <summary>
    /// Gets the soft delete flag change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSoftDeleted { get; init; }

    /// <summary>
    /// Gets the soft delete time change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> SoftDeleted { get; init; }

    /// <summary>
    /// Gets the hard delete flag change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsHardDeleted { get; init; }

    /// <summary>
    /// Gets the hard delete time change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> HardDeleted { get; init; }

    /// <summary>
    /// Gets the read status change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadStatus? ReadStatus { get; init; }

    /// <summary>
    /// Gets the replacement substatus. Null omits the replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Substatus? Substatus { get; init; }
}
