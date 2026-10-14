using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Populates instance owners from Altinn Register.
/// </summary>
public interface IInstanceOwnerService
{
    /// <summary>
    /// Gets the instance owner for the given party from Register.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The instance owner, or <c>null</c> if Register has no party with the given id</returns>
    Task<InstanceOwner?> GetInstanceOwner(int partyId, CancellationToken cancellationToken);

    /// <summary>
    /// Populates the owner of an instance whose owner has not been populated from Register, and stores
    /// it for every instance of the party.
    /// </summary>
    /// <remarks>
    /// An owner with a <see cref="InstanceOwner.PartyUuid"/> has been populated from Register. Failures
    /// are logged and leave the instance owner as it was, so reading an instance never fails because of it.
    /// </remarks>
    /// <param name="instance">The instance, whose owner is replaced when populated</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task PopulateMissingInstanceOwner(
        InstanceInternal instance,
        CancellationToken cancellationToken
    );
}
