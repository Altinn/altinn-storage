using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class InstanceQueryParametersTests
{
    [Fact]
    public void ParseInstanceOwnerIdentifier_ValidPerson_ReturnsPerson()
    {
        // Arrange
        InstanceQueryParameters parameters = new()
        {
            InstanceOwnerIdentifier = "Person:33312321321",
        };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("33312321321", result.Person);
        Assert.Null(result.OrgNo);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ParseInstanceOwnerIdentifier_ValidOrganisation_ReturnsOrgNo()
    {
        // Arrange
        InstanceQueryParameters parameters = new()
        {
            InstanceOwnerIdentifier = "Organisation:333123213",
        };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("333123213", result.OrgNo);
        Assert.Null(result.Person);
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
