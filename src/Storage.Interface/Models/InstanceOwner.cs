using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// Represents information to identify the owner of an instance.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class InstanceOwner
{
    /// <summary>
    /// Gets or sets the party id of the instance owner (also called instance owner party id).
    /// </summary>
    [JsonProperty(PropertyName = "partyId")]
    public string PartyId { get; set; }

    /// <summary>
    /// Gets or sets person number (national identification number) of the party. Null if the party is not a person.
    /// </summary>
    [JsonProperty(PropertyName = "personNumber")]
    public string PersonNumber { get; set; }

    /// <summary>
    /// Gets or sets the organisation number of the party. Null if the party is not an organisation.
    /// </summary>
    [JsonProperty(PropertyName = "organisationNumber")]
    public string OrganisationNumber { get; set; }

    /// <summary>
    /// Gets or sets the username of the party. Only set for legacy username-based authentication.
    /// Null for email-based self-identified users.
    /// Altinn 3 will use ID-Porten self identified users, which are email based.
    /// This property will be removed when Altinn 3 no longer supports username based authentication.
    /// </summary>
    [JsonProperty(PropertyName = "username")]
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the email of the party. This property is only set for self identified users.
    /// </summary>
    [JsonProperty(PropertyName = "email")]
    public string Email { get; set; }
}
