using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class InstanceQueryParametersTests
{
    [Theory]
    [InlineData("Person:25871999336", "urn:altinn:person:identifier-no:25871999336")]
    [InlineData("person: 25871999336", "urn:altinn:person:identifier-no:25871999336")]
    [InlineData("Organisation:974760673", "urn:altinn:organization:identifier-no:974760673")]
    public void ParseInstanceOwnerIdentifier_ValidShortForm_ReturnsPartyUrn(
        string identifier,
        string expectedUrn
    )
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(expectedUrn, result.Identifier?.ToString());
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("urn:altinn:party:id:1337")]
    [InlineData("urn:altinn:party:uuid:4c3b4909-eb17-45d5-bde1-256e065e196a")]
    [InlineData("urn:altinn:person:identifier-no:25871999336")]
    [InlineData("urn:altinn:organization:identifier-no:974760673")]
    [InlineData("urn:altinn:party:username:jdoe")]
    [InlineData("urn:altinn:person:idporten-email:jdoe@example.com")]
    public void ParseInstanceOwnerIdentifier_PartyUrn_ReturnsPartyUrn(string identifier)
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(identifier, result.Identifier?.ToString());
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("Person:33312321321")]
    [InlineData("Organisation:333123213")]
    public void ParseInstanceOwnerIdentifier_InvalidCheckDigit_ReturnsValidWithoutIdentifier(
        string identifier
    )
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.Identifier);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ParseInstanceOwnerIdentifier_PersonWrongLength_ReturnsError()
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = "Person:33312" };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Person number needs to be exactly 11 digits.", result.ErrorMessage);
    }

    [Fact]
    public void ParseInstanceOwnerIdentifier_OrganisationWrongLength_ReturnsError()
    {
        // Arrange
        InstanceQueryParameters parameters = new()
        {
            InstanceOwnerIdentifier = "Organisation:33312",
        };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Organization number needs to be exactly 9 digits.", result.ErrorMessage);
    }

    [Theory]
    [InlineData("something:3312321321")]
    [InlineData("33312321321")]
    [InlineData("")]
    [InlineData("urn:altinn:unknown:1337")]
    [InlineData("urn:altinn:party:uuid:not-a-uuid")]
    [InlineData("urn:altinn:person:identifier-no:33312321321")]
    public void ParseInstanceOwnerIdentifier_UnknownOrMalformed_ReturnsInvalid(string identifier)
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Invalid InstanceOwnerIdentifier.", result.ErrorMessage);
    }
}
