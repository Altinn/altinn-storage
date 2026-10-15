#nullable disable

using System;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// Represents information to identify the owner of an instance.
/// </summary>
/// <remarks>
/// When an instance is created, Storage identifies the owner by the first of <see cref="PartyId"/>,
/// <see cref="ExternalIdentifier"/>, <see cref="PersonNumber"/>, <see cref="OrganisationNumber"/> and
/// <see cref="Username"/> that has a value.
/// All properties are then populated by Storage from Altinn Register.
/// </remarks>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class InstanceOwner
{
    /// <summary>
    /// Gets or sets the party id of the instance owner (also called instance owner party id).
    /// </summary>
    [JsonProperty(PropertyName = "partyId")]
    public string PartyId { get; set; }

    /// <summary>
    /// Gets or sets the party uuid of the instance owner.
    /// </summary>
    [JsonProperty(PropertyName = "partyUuid")]
    public Guid? PartyUuid { get; set; }

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
    /// Gets or sets the username of the party.
    /// For self identifier users.
    /// Legacy sets this as a username, new id porten email self identification sets this as email on the form "epost:[email]"
    /// </summary>
    [JsonProperty(PropertyName = "username")]
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets an external identifier for the party.
    /// This is used for self identifier users, on the form "urn:altinn:person:idporten-email:[email]"
    /// or "urn:altinn:person:legacy-selfidentified:[username]".
    /// </summary>
    [JsonProperty(PropertyName = "externalIdentifier")]
    public string ExternalIdentifier { get; set; }
}
