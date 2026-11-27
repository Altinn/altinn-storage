using Microsoft.AspNetCore.Authorization;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Requires a valid platform access token from one issuer, naming one app in its
/// <c>urn:altinn:app</c> claim.
/// </summary>
/// <remarks>
/// Platform components share the signing key of their issuer, so the app claim identifies the
/// component only as far as the components holding that key can be trusted to set it correctly.
/// </remarks>
/// <param name="Issuer">The issuer the token must be signed by, such as <c>platform</c>.</param>
/// <param name="App">The app the token must name, such as <c>file-scan</c>.</param>
public sealed record PlatformAccessTokenAppRequirement(string Issuer, string App)
    : IAuthorizationRequirement;
