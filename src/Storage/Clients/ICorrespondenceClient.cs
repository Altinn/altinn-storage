using System;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Clients;

/// <summary>
/// Interface for actions related to the parties with instances resource in SBL.
/// </summary>
public interface ICorrespondenceClient
{
    /// <summary>
    /// Call SBL to sync a correspondence event from Altinn 3 with an Altinn 2 correspondence.
    /// </summary>
    /// <param name="correspondenceId">Altinn 2 ServiceEngine correspondence Id.</param>
    /// <param name="partyId">The party id of the user.</param>
    /// <param name="eventTimestamp">Timestamp that the event took place in Altinn 3 (UTC Time)</param>
    /// <param name="eventType">Event type</param>
    /// <returns></returns>
    Task SyncCorrespondenceEvent(int correspondenceId, int partyId, DateTimeOffset eventTimestamp, string eventType);
}