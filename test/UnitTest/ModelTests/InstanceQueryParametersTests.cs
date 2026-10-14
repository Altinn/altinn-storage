using Altinn.Platform.Storage.Models;
using Altinn.Register.Contracts;
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
        PartyUrn? result = parameters.ParseInstanceOwnerIdentifier(out string? errorMessage);

        // Assert
        Assert.Equal(expectedUrn, result?.ToString());
        Assert.Null(errorMessage);
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
        PartyUrn? result = parameters.ParseInstanceOwnerIdentifier(out string? errorMessage);

        // Assert
        Assert.Equal(identifier, result?.ToString());
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData("Person:33312", "Person number needs to be exactly 11 digits.")]
    [InlineData("Organisation:33312", "Organization number needs to be exactly 9 digits.")]
    [InlineData("Person:33312321321", "Invalid person number.")]
    [InlineData("Organisation:333123213", "Invalid organisation number.")]
    [InlineData("something:3312321321", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("2:974760673", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("33312321321", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("urn:altinn:unknown:1337", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("urn:altinn:party:uuid:not-a-uuid", "Invalid InstanceOwnerIdentifier.")]
    [InlineData("urn:altinn:person:identifier-no:33312321321", "Invalid InstanceOwnerIdentifier.")]
    public void ParseInstanceOwnerIdentifier_Invalid_ReturnsNullWithErrorMessage(
        string identifier,
        string expectedErrorMessage
    )
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        PartyUrn? result = parameters.ParseInstanceOwnerIdentifier(out string? errorMessage);

        // Assert
        Assert.Null(result);
        Assert.Equal(expectedErrorMessage, errorMessage);
    }
}
