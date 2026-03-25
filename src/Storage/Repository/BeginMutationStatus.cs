#nullable enable

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Result of attempting to begin a data mutation.
/// </summary>
public enum BeginMutationStatus
{
    /// <summary>
    /// Mutation was allowed; a tracking row was inserted.
    /// </summary>
    Ok,

    /// <summary>
    /// Mutation was blocked by an active lock with preventMutations enabled.
    /// </summary>
    MutationBlocked,

    /// <summary>
    /// The instance was not found.
    /// </summary>
    InstanceNotFound,
}
