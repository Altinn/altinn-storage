using System.Security.Claims;
using Altinn.Platform.Storage.Helpers;
using AltinnCore.Authentication.Constants;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void TryParseAuthenticationLevel_ReturnsFalseWhenParseFails()
    {
        // Arrange
        ClaimsIdentity identity = new();
        ClaimsPrincipal principal = new();
        principal.AddIdentity(identity);

        // Act
        bool result = principal.TryParseAuthenticationLevel(out int authenticationLevel);

        // Assert
        Assert.False(result);
        Assert.Equal(0, authenticationLevel);
    }

    [Fact]
    public void TryParseAuthenticationLevel_ReturnsTrueWhenParseSucceeds()
    {
        // Arrange
        Claim authenticationLevelClaim = new(AltinnCoreClaimTypes.AuthenticationLevel, "3");

        ClaimsIdentity identity = new();
        identity.AddClaim(authenticationLevelClaim);

        ClaimsPrincipal principal = new();
        principal.AddIdentity(identity);

        // Act
        bool result = principal.TryParseAuthenticationLevel(out int authenticationLevel);

        // Assert
        Assert.True(result);
        Assert.Equal(3, authenticationLevel);
    }
}
