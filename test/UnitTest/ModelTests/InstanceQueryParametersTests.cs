using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class InstanceQueryParametersTests
{
    [Fact]
    public void ApplyQueryDefaults_EmptyParameters_SetsDefaults()
    {
        // Arrange
        InstanceQueryParameters parameters = new();

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Equal("desc:lastChanged", parameters.SortBy);
        Assert.Equal(3, parameters.MainVersionInclude);
        Assert.Null(parameters.MainVersionExclude);
        Assert.Equal(100, parameters.Size);
    }

    [Fact]
    public void ApplyQueryDefaults_ExplicitValues_ArePreserved()
    {
        // Arrange
        InstanceQueryParameters parameters = new() { SortBy = "asc:created", Size = 42 };

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Equal("asc:created", parameters.SortBy);
        Assert.Equal(42, parameters.Size);
    }

    [Fact]
    public void ApplyQueryDefaults_DataValuesA2ArchRef_ForcesMainVersion2()
    {
        // Arrange
        InstanceQueryParameters parameters = new() { DataValuesA2ArchRef = "ABC123" };

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Equal(2, parameters.MainVersionInclude);
        Assert.Null(parameters.MainVersionExclude);
    }

    [Fact]
    public void ApplyQueryDefaults_DataValuesA2ArchRef_OverridesExistingMainVersion()
    {
        // Arrange
        InstanceQueryParameters parameters = new()
        {
            DataValuesA2ArchRef = "ABC123",
            MainVersionInclude = 3,
            MainVersionExclude = 1,
        };

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Equal(2, parameters.MainVersionInclude);
        Assert.Null(parameters.MainVersionExclude);
    }

    [Fact]
    public void ApplyQueryDefaults_ExplicitMainVersionInclude_IsPreserved()
    {
        // Arrange
        InstanceQueryParameters parameters = new() { MainVersionInclude = 2 };

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Equal(2, parameters.MainVersionInclude);
        Assert.Null(parameters.MainVersionExclude);
    }

    [Fact]
    public void ApplyQueryDefaults_ExplicitMainVersionExclude_SuppressesDefaultInclude()
    {
        // Arrange
        InstanceQueryParameters parameters = new() { MainVersionExclude = 2 };

        // Act
        parameters.ApplyQueryDefaults();

        // Assert
        Assert.Null(parameters.MainVersionInclude);
        Assert.Equal(2, parameters.MainVersionExclude);
    }

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
