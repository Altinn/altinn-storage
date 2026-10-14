using System;
using System.Globalization;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Register.Contracts;
using Altinn.Urn;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Maps between parties in Altinn Register and instance owners.
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

    /// <summary>
    /// Gets the Register party URN that identifies the owner given when an instance is created.
    /// </summary>
    /// <remarks>
    /// Uses the first property that has a value, in the same order as Altinn apps:
    /// <see cref="InstanceOwner.PartyId"/>, <see cref="InstanceOwner.ExternalIdentifier"/> (any party URN),
    /// <see cref="InstanceOwner.PersonNumber"/>, <see cref="InstanceOwner.OrganisationNumber"/> and
    /// <see cref="InstanceOwner.Username"/>, which is the email address of an ID-porten email user, with or
    /// without the <c>epost:</c> prefix.
    /// </remarks>
    /// <param name="owner">The instance owner from the request.</param>
    /// <param name="errorMessage">A description of the problem when no party URN can be created; otherwise null.</param>
    /// <returns>The party URN, or null when the owner has no valid identifier.</returns>
    public static PartyUrn? ToPartyUrn(InstanceOwner? owner, out string? errorMessage)
    {
        errorMessage = null;

        if (!string.IsNullOrWhiteSpace(owner?.PartyId))
        {
            if (
                int.TryParse(
                    owner.PartyId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int partyId
                )
            )
            {
                return PartyUrn.PartyId.Create((uint)partyId);
            }

            errorMessage = "Invalid instanceOwner.PartyId.";
        }
        else if (!string.IsNullOrWhiteSpace(owner?.ExternalIdentifier))
        {
            if (PartyUrn.TryParse(owner.ExternalIdentifier, null, out PartyUrn? partyUrn))
            {
                return partyUrn;
            }

            errorMessage = "Invalid instanceOwner.ExternalIdentifier.";
        }
        else if (!string.IsNullOrWhiteSpace(owner?.PersonNumber))
        {
            if (PersonIdentifier.TryParse(owner.PersonNumber, null, out PersonIdentifier? person))
            {
                return PartyUrn.PersonId.Create(person);
            }

            errorMessage = "Invalid instanceOwner.PersonNumber.";
        }
        else if (!string.IsNullOrWhiteSpace(owner?.OrganisationNumber))
        {
            if (
                OrganizationIdentifier.TryParse(
                    owner.OrganisationNumber,
                    null,
                    out OrganizationIdentifier? organization
                )
            )
            {
                return PartyUrn.OrganizationId.Create(organization);
            }

            errorMessage = "Invalid instanceOwner.OrganisationNumber.";
        }
        else if (!string.IsNullOrWhiteSpace(owner?.Username))
        {
            string email = owner.Username.StartsWith(
                _emailUsernamePrefix,
                StringComparison.OrdinalIgnoreCase
            )
                ? owner.Username[_emailUsernamePrefix.Length..]
                : owner.Username;
            return PartyUrn.IDPortenEmail.Create(UrnEncoded.Create(email));
        }
        else
        {
            errorMessage =
                "Cannot create an instance without an instanceOwner.PartyId, ExternalIdentifier, PersonNumber, OrganisationNumber or Username.";
        }

        return null;
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
            _ when username.StartsWith(_emailUsernamePrefix, StringComparison.Ordinal) =>
                _idPortenEmailUrnPrefix + username[_emailUsernamePrefix.Length..],
            _ => _legacySelfIdentifiedUrnPrefix + username,
        };
}
