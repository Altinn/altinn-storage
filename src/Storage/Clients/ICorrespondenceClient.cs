using System;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Clients
{
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
        /// <summary>
/// Instructs SBL to synchronize an Altinn 3 correspondence event with the Altinn 2 correspondence identified by <paramref name="correspondenceId"/>.
/// </summary>
/// <param name="correspondenceId">Altinn 2 ServiceEngine correspondence identifier.</param>
/// <param name="partyId">The party id of the user that the correspondence belongs to.</param>
/// <param name="eventTimestamp">The timestamp (UTC) when the event occurred in Altinn 3.</param>
/// <param name="eventType">Type of the event to synchronize (e.g., created, delivered, read).</param>
Task SyncCorrespondenceEvent(int correspondenceId, int partyId, DateTimeOffset eventTimestamp, string eventType);
        Task SyncCorrespondenceEvent(int correspondenceId, int partyId, DateTimeOffset eventTimestamp, string eventType);
    }
}
