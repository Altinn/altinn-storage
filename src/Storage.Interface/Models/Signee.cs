using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{

    /// <summary>
    /// Information about the signee
    /// </summary>
    public class Signee
    {
        /// <summary>
        /// The userId representing the signee.
        /// </summary>
        [JsonProperty(PropertyName = "userId")]
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// The personNumber representing the signee.
        /// </summary>
        [JsonProperty(PropertyName = "personNumber")]
        public string? PersonNumber { get; set; }

        /// <summary>
        /// The organisationNumber representing the signee.
        /// </summary>
        [JsonProperty(PropertyName = "organisationNumber")]
        public string? OrganisationNumber { get; set; }
    }
}
