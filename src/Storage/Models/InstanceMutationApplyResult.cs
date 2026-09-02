using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from applying an aggregate mutation.
/// </summary>
public sealed record InstanceMutationApplyResult(
    bool Replayed,
    IReadOnlyList<string> CreatedDataElementIds,
    InstanceInternal Instance
);
