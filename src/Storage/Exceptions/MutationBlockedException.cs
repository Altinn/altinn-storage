#nullable enable

using System;

namespace Altinn.Platform.Storage.Exceptions;

/// <summary>
/// Exception thrown when a data mutation is blocked by an active instance lock
/// with preventMutations enabled.
/// </summary>
public sealed class MutationBlockedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MutationBlockedException"/> class.
    /// </summary>
    public MutationBlockedException()
        : base(
            "Data mutations are blocked by an active instance lock with preventMutations enabled. "
                + "Provide a valid lock token in the Altinn-Storage-Lock-Token header."
        ) { }
}
