using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Instance metadata changes using Storage's merge and replacement semantics.
/// </summary>
public sealed record InstanceMutationChanges
{
    /// <summary>
    /// Gets the creation time change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> Created { get; init; }

    /// <summary>
    /// Gets the creator change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<string?> CreatedBy { get; init; }

    /// <summary>
    /// Gets the due date change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> DueBefore { get; init; }

    /// <summary>
    /// Gets the visibility time change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DateTime?> VisibleAfter { get; init; }

    /// <summary>
    /// Gets data values to merge. Null omits the merge; null entries remove keys.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string?>? DataValues { get; init; }

    /// <summary>
    /// Gets presentation texts to merge. Null omits the merge; null entries remove keys.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string?>? PresentationTexts { get; init; }

    /// <summary>
    /// Gets confirmations to append for stakeholders that have not already confirmed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CompleteConfirmation>? CompleteConfirmations { get; init; }

    /// <summary>
    /// Gets the confirmed flag change. Null derives the flag from added organisation confirmations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Confirmed { get; init; }

    /// <summary>
    /// Gets the partial status changes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstanceMutationStatusChanges? Status { get; init; }

    /// <summary>
    /// Gets the replacement process state. Null omits the process update.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessState? Process { get; init; }
}
