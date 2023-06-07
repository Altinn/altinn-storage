namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Contains information about a user context
    /// </summary>
    public class UserContext
    {
        /// <summary>
        /// Gets or sets the ID of the user
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// Gets or sets the party ID
        /// </summary>
        public string PartyId { get; set; }

        /// <summary>
        /// Gets or sets the orgnr
        /// </summary>
        public int? Orgnr { get; set; }
    }
}