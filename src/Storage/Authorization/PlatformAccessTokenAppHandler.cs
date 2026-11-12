using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Constants;
using Altinn.Common.AccessToken.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Succeeds <see cref="PlatformAccessTokenAppRequirement"/> when the request carries exactly one platform
/// access token that is signed by the required issuer, is within its lifetime and names the required app.
/// </summary>
/// <remarks>
/// The token is validated here rather than relying on the result of the access token handler, so the
/// requirement holds on its own whatever other requirements a policy has. Unlike the access token handler,
/// <see cref="AccessTokenSettings.DisableAccessTokenVerification"/> does not apply: endpoints with this
/// requirement are never open without a token.
/// </remarks>
public sealed class PlatformAccessTokenAppHandler(
    IHttpContextAccessor httpContextAccessor,
    IAccessTokenValidator accessTokenValidator,
    IOptions<AccessTokenSettings> accessTokenSettings,
    ILogger<PlatformAccessTokenAppHandler> logger
) : AuthorizationHandler<PlatformAccessTokenAppRequirement>
{
    private readonly string _headerName = accessTokenSettings.Value.AccessTokenHeaderId;

    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAccessTokenAppRequirement requirement
    )
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (
            httpContext is null
            || !httpContext.Request.Headers.TryGetValue(_headerName, out StringValues tokens)
            || tokens.Count != 1
            || string.IsNullOrEmpty(tokens[0])
        )
        {
            return;
        }

        string token = tokens[0]!;
        JwtSecurityTokenHandler tokenHandler = new();
        if (!tokenHandler.CanReadToken(token))
        {
            return;
        }

        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(token);
        string? app = jwt
            .Claims.FirstOrDefault(claim => claim.Type == AccessTokenClaimTypes.App)
            ?.Value;
        if (jwt.Issuer != requirement.Issuer || app != requirement.App)
        {
            logger.LogWarning(
                "Platform access token from issuer {Issuer} and app {App} is not accepted where {RequiredIssuer}/{RequiredApp} is required.",
                jwt.Issuer,
                app,
                requirement.Issuer,
                requirement.App
            );
            return;
        }

        if (await accessTokenValidator.Validate(token))
        {
            context.Succeed(requirement);
        }
    }
}
