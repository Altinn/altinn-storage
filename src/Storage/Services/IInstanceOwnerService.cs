using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Register.Contracts;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Populates instance owners from Altinn Register.
/// </summary>
public interface IInstanceOwnerService
{
    /// <summary>
    /// Gets the instance owner for the party identified by the given party URN from Register.
    /// </summary>
    /// <param name="identifier">Any party URN supported by Register, such as a party id or person number</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The instance owner, or <c>null</c> if Register has no matching party</returns>
    Task<InstanceOwner?> GetInstanceOwner(PartyUrn identifier, CancellationToken cancellationToken);

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
