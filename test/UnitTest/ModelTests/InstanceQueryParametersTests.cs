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
    [InlineData("Organisation:\uFF19\uFF19\uFF13\uFF16\uFF10\uFF16\uFF16\uFF15\uFF10")]
    [InlineData("Organisation:\u0669\u0669\u0663\u0666\u0660\u0666\u0666\u0665\u0660")]
    public void ParseInstanceOwnerIdentifier_OrganisationNonAsciiDigits_ReturnsError(
        string identifier
    )
    {
        // Arrange
        InstanceQueryParameters parameters = new() { InstanceOwnerIdentifier = identifier };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Organization number needs to be exactly 9 digits.", result.ErrorMessage);
    }

    [Fact]
    public void ParseInstanceOwnerIdentifier_PersonNonAsciiDigits_ReturnsError()
    {
        // Arrange
        InstanceQueryParameters parameters = new()
        {
            InstanceOwnerIdentifier =
                "Person:\uFF13\uFF13\uFF13\uFF11\uFF12\uFF13\uFF12\uFF11\uFF13\uFF12\uFF11",
        };

        // Act
        InstanceOwnerIdentifierParseResult result = parameters.ParseInstanceOwnerIdentifier();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Person number needs to be exactly 11 digits.", result.ErrorMessage);
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
