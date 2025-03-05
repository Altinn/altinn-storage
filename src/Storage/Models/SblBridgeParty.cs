using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Represents a party in SBL Bridge
    /// </summary>
    public class SblBridgeParty
    {
        /// <summary>
        /// Gets or sets the party id.
        /// </summary>
        [JsonProperty(PropertyName = "partyId")]
        public int PartyId { get; set; }
    }
}
