using System.Threading;
using System.Threading.Tasks;
using Altinn.Register.Contracts;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Interface to handle services exposed in Platform Register
/// </summary>
public interface IRegisterService
{
    /// <summary>
    /// Gets the party with the given party id, including the identifiers Storage needs for an instance owner.
    /// </summary>
    /// <param name="partyId">The party id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The party, or <c>null</c> if Register has no party with the given id</returns>
    Task<Party?> GetParty(int partyId, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up the party id of the party identified by the given party URN.
    /// </summary>
    /// <param name="identifier">Any party URN supported by Register, such as a person or organisation number</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The party id, or <c>null</c> if Register has no matching party</returns>
    Task<int?> LookupPartyId(PartyUrn identifier, CancellationToken cancellationToken);
}
