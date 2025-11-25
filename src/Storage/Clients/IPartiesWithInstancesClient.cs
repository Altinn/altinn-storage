using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Clients;

/// <summary>
/// Interface for actions related to the parties with instances resource in SBL.
/// </summary>
public interface IPartiesWithInstancesClient
{
    /// <summary>
    /// Call SBL to inform about a party getting an instance of an app.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <returns>Nothing is returned.</returns>
    Task SetHasAltinn3Instances(int instanceOwnerPartyId);

    /// <summary>
    /// Call SBL to inform that a party has correspondences in Altinn 3 Correspondence
    /// </summary>
    /// <param name="partyId">The party id of the recipient</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task SetHasAltinn3Correspondence(int partyId);
}