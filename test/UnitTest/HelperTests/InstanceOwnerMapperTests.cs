using System;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class InstanceOwnerMapperTests
{
    private const string _partyUuid = "4c3b4909-eb17-45d5-bde1-256e065e196a";

    [Fact]
    public void FromParty_Person_SetsPersonNumber()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "person",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "Test Testesen",
              "personIdentifier": "25871999336",
              "externalUrn": "urn:altinn:person:identifier-no:25871999336"
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(owner, personNumber: "25871999336");
    }

    [Fact]
    public void FromParty_Organization_SetsOrganisationNumber()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "organization",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "Testbedrift AS",
              "organizationIdentifier": "974760673",
              "externalUrn": "urn:altinn:organization:identifier-no:974760673"
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(owner, organisationNumber: "974760673");
    }

    [Fact]
    public void FromParty_IdPortenEmailUserWithUsername_SetsUsernameAndExternalUrn()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "self-identified-user",
              "selfIdentifiedUserType": "idporten-email",
              "email": "test@example.com",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "test@example.com",
              "externalUrn": "urn:altinn:person:idporten-email:test@example.com",
              "user": { "userId": 50, "username": "username", "userIds": [50], "usernames": ["username"] }
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(
            owner,
            username: "username",
            externalIdentifier: "urn:altinn:person:idporten-email:test@example.com"
        );
    }

    [Fact]
    public void FromParty_IdPortenEmailUserWithoutUsername_UsesEmailUsername()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "self-identified-user",
              "selfIdentifiedUserType": "idporten-email",
              "email": "test@example.com",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "test@example.com",
              "externalUrn": "urn:altinn:person:idporten-email:test@example.com",
              "user": { "userId": 50, "userIds": [50] }
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(
            owner,
            username: "epost:test@example.com",
            externalIdentifier: "urn:altinn:person:idporten-email:test@example.com"
        );
    }

    [Fact]
    public void FromParty_LegacySelfIdentifiedUser_SetsUsernameAndExternalUrn()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "self-identified-user",
              "selfIdentifiedUserType": "legacy",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "Display Name",
              "externalUrn": "urn:altinn:person:legacy-selfidentified:username",
              "user": { "userId": 50, "username": "username", "userIds": [50], "usernames": ["username"] }
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(
            owner,
            username: "username",
            externalIdentifier: "urn:altinn:person:legacy-selfidentified:username"
        );
    }

    [Fact]
    public void FromParty_SelfIdentifiedUserWithoutUserOrExternalUrn_DerivesFromDisplayName()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "self-identified-user",
              "selfIdentifiedUserType": "legacy",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "Display Name"
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(
            owner,
            username: "Display Name",
            externalIdentifier: "urn:altinn:person:legacy-selfidentified:Display Name"
        );
    }

    [Fact]
    public void FromParty_SystemUser_SetsOnlyPartyIdentifiers()
    {
        // Arrange
        string json = $$"""
            {
              "partyType": "system-user",
              "partyUuid": "{{_partyUuid}}",
              "versionId": 1,
              "partyId": 1337,
              "displayName": "System user"
            }
            """;

        // Act
        InstanceOwner owner = InstanceOwnerMapper.FromParty(RegisterServiceMock.Deserialize(json));

        // Assert
        AssertOwner(owner);
    }

    private static void AssertOwner(
        InstanceOwner owner,
        string? personNumber = null,
        string? organisationNumber = null,
        string? username = null,
        string? externalIdentifier = null
    )
    {
        Assert.Equal("1337", owner.PartyId);
        Assert.Equal(Guid.Parse(_partyUuid), owner.PartyUuid);
        Assert.Equal(personNumber, owner.PersonNumber);
        Assert.Equal(organisationNumber, owner.OrganisationNumber);
        Assert.Equal(username, owner.Username);
        Assert.Equal(externalIdentifier, owner.ExternalIdentifier);
    }
}
