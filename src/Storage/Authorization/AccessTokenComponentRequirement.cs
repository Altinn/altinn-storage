using Microsoft.AspNetCore.Authorization;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Requires the platform access token on the request to come from the given platform component.
/// </summary>
/// <remarks>
/// The component is read from the token's <c>urn:altinn:app</c> claim, which platform components
/// set to their own name. This requirement does not validate the token. Combine it with an
/// <see cref="Altinn.Common.AccessToken.AccessTokenRequirement"/> in the same policy, which validates
/// the signature and issuer of the same token.
/// </remarks>
/// <param name="component">The name of the required platform component, such as <c>file-scan</c>.</param>
public class AccessTokenComponentRequirement(string component) : IAuthorizationRequirement
{
    /// <summary>
    /// Gets the name of the required platform component.
    /// </summary>
    public string Component { get; } = component;
}
