#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Tracks active data mutation requests to coordinate with instance locking.
/// </summary>
public interface IDataMutationTracker
{
    /// <summary>
    /// Begins tracking a data mutation. Returns an <see cref="IAsyncDisposable"/> that
    /// ends the tracking when disposed.
    /// </summary>
    /// <param name="instanceGuid">The instance GUID.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that ends the mutation tracking on disposal.</returns>
    /// <exception cref="Altinn.Platform.Storage.Exceptions.MutationBlockedException">
    /// Thrown when mutations are blocked by an active instance lock with preventMutations enabled.
    /// </exception>
    Task<IAsyncDisposable> BeginMutation(
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    );
}
