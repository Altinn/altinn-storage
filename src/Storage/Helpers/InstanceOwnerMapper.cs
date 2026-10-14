using System.Globalization;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Register.Contracts;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Maps parties from Altinn Register to instance owners.
/// </summary>
/// <remarks>
/// The identifiers match what Altinn apps have stored on instances they created, which they derive from
/// the Register v1 party model. Register builds that model from the same data, so for example a
/// self-identified user's <see cref="InstanceOwner.Username"/> is its username, falling back to
/// <c>epost:{email}</c> for ID-porten email users and then to the display name.
/// </remarks>
public static class InstanceOwnerMapper
{
    private const string _legacySelfIdentifiedUrnPrefix =
        "urn:altinn:person:legacy-selfidentified:";
    private const string _idPortenEmailUrnPrefix = "urn:altinn:person:idporten-email:";
    private const string _emailUsernamePrefix = "epost:";

    /// <summary>
    /// Creates the instance owner for the given party.
    /// </summary>
    /// <param name="party">The party, fetched with <c>identifiers</c>, <c>type</c>, <c>display-name</c>, <c>user</c> and <c>si</c> fields.</param>
    /// <returns>The instance owner.</returns>
    public static InstanceOwner FromParty(Party party)
    {
        InstanceOwner owner = new()
        {
            PartyId = party.PartyId.Value.ToString(CultureInfo.InvariantCulture),
            PartyUuid = party.Uuid,
        };

        switch (party)
        {
            case Person person:
                owner.PersonNumber = person.PersonIdentifier.ToString();
                break;

            case Organization organization:
                owner.OrganisationNumber = organization.OrganizationIdentifier.ToString();
                break;

            case SelfIdentifiedUser selfIdentifiedUser:
                owner.Username = GetSelfIdentifiedUsername(selfIdentifiedUser);
                owner.ExternalIdentifier =
                    selfIdentifiedUser.ExternalUrn.Value?.ToString()
                    ?? GetExternalIdentifierFromUsername(owner.Username);
                break;
        }

        return owner;
    }

    private static string? GetSelfIdentifiedUsername(SelfIdentifiedUser party)
    {
        string? username = party.User.Value?.Username.Value;
        if (!string.IsNullOrEmpty(username))
        {
            return username;
        }

        bool isIdPortenEmailUser =
            party.SelfIdentifiedUserType.Value.TryGetValue(out SelfIdentifiedUserType type)
            && type == SelfIdentifiedUserType.IdPortenEmail;

        if (isIdPortenEmailUser && !string.IsNullOrEmpty(party.Email.Value))
        {
            return _emailUsernamePrefix + party.Email.Value;
        }

        return party.DisplayName.Value;
    }

    private static string? GetExternalIdentifierFromUsername(string? username) =>
        username switch
        {
            null or "" => null,
            _ when username.StartsWith(_emailUsernamePrefix, System.StringComparison.Ordinal) =>
                _idPortenEmailUrnPrefix + username[_emailUsernamePrefix.Length..],
            _ => _legacySelfIdentifiedUrnPrefix + username,
        };
}
