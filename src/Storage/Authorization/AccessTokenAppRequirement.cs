using Microsoft.AspNetCore.Authorization;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Requires the platform access token on the request to carry the given app claim.
/// </summary>
/// <remarks>
/// This requirement does not validate the token. Combine it with an
/// <see cref="Altinn.Common.AccessToken.AccessTokenRequirement"/> in the same policy, which validates
/// the signature and issuer of the same token.
/// </remarks>
/// <param name="app">The required value of the app claim.</param>
public class AccessTokenAppRequirement(string app) : IAuthorizationRequirement
{
    /// <summary>
    /// Gets the required value of the app claim.
    /// </summary>
    public string App { get; } = app;
}
