#nullable enable

using System.Security.Claims;

using AltinnCore.Authentication.Constants;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Helper methods to extend ClaimsPrincipal.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the userId or the orgNumber or null if neither claims are present.
    /// </summary>
    public static string? GetUserOrOrgId(this ClaimsPrincipal user)
    {
        int? userId = GetUserIdAsInt(user);
        if (userId.HasValue)
        {
            return userId.Value.ToString();
        }

        string? orgId = GetOrgNumber(user);
        if (orgId is not null)
        {
            return orgId;
        }

        return null;
    }

    /// <summary>
    /// Get the org identifier string or null if it is not an org.
    /// </summary>
    public static string? GetOrg(this ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == AltinnCoreClaimTypes.Org))
        {
            Claim? orgClaim = user.FindFirst(c => c.Type == AltinnCoreClaimTypes.Org);
            if (orgClaim is not null)
            {
                return orgClaim.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the organisation number of an org user or null if claim does not exist.
    /// </summary>
    public static string? GetOrgNumber(this ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == AltinnCoreClaimTypes.OrgNumber))
        {
            Claim? orgClaim = user.FindFirst(c => c.Type == AltinnCoreClaimTypes.OrgNumber);
            if (orgClaim is not null)
            {
                return orgClaim.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Return the userId as an int or null if UserId claim is not set
    /// </summary>
    public static int? GetUserIdAsInt(this ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == AltinnCoreClaimTypes.UserId))
        {
            Claim? userIdClaim = user.FindFirst(c => c.Type == AltinnCoreClaimTypes.UserId);
            if (userIdClaim is not null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the authentication level of the user.
    /// </summary>
    public static int GetAuthenticationLevel(this ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == AltinnCoreClaimTypes.AuthenticationLevel))
        {
            Claim? userIdClaim = user.FindFirst(c => c.Type == AltinnCoreClaimTypes.AuthenticationLevel);
            if (userIdClaim is not null && int.TryParse(userIdClaim.Value, out int authenticationLevel))
            {
                return authenticationLevel;
            }
        }

        return 0;
    }
}
