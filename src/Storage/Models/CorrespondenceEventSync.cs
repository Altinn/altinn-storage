using System;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Represents an event taking place in Altinn 3 to be synced via SBLBridge
/// </summary>
public class CorrespondenceEventSync
{
    /// <summary>
    /// Gets or sets the Altinn 2 ServiceEngine correspondence Id.
    /// </summary>
    [JsonProperty(PropertyName = "correspondenceId")]
    public int CorrespondenceId { get; set; }

    /// <summary>
    /// Gets or sets the party id of the user causing the event.
    /// </summary>
    [JsonProperty(PropertyName = "partyId")]
    public int PartyId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the event. Timestamp should always be UTC time.
    /// </summary>
    [JsonProperty(PropertyName = "eventTimestamp")]
    public DateTimeOffset EventTimeStamp { get; set; }

    /// <summary>
    /// Gets or sets the Correspondence Event Type. (Expects Read, Confirm or Delete).
    /// </summary>
    [JsonProperty(PropertyName = "eventType")]
    public string EventType { get; set; }
}
