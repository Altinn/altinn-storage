using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Checks the component in the platform access token against an <see cref="AccessTokenComponentRequirement"/>.
/// </summary>
/// <remarks>
/// The token is read without validation. The policy must also contain an
/// <see cref="Altinn.Common.AccessToken.AccessTokenRequirement"/>, whose handler validates the same
/// token and accepts exactly one. Reading the header here, instead of the result that handler stores
/// on the request, keeps the check independent of the order the handlers run in.
/// </remarks>
/// <param name="httpContextAccessor">Access to the current request.</param>
/// <param name="accessTokenSettings">The access token settings.</param>
public class AccessTokenComponentHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AccessTokenSettings> accessTokenSettings
) : AuthorizationHandler<AccessTokenComponentRequirement>
{
    private readonly AccessTokenSettings _settings = accessTokenSettings.Value;

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AccessTokenComponentRequirement requirement
    )
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        StringValues tokens = httpContext.Request.Headers[_settings.AccessTokenHeaderId];

        if (tokens.Count == 0 && _settings.DisableAccessTokenVerification)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (tokens.Count != 1 || string.IsNullOrEmpty(tokens[0]))
        {
            return Task.CompletedTask;
        }

        JwtSecurityTokenHandler tokenHandler = new();
        if (!tokenHandler.CanReadToken(tokens[0]))
        {
            return Task.CompletedTask;
        }

        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(tokens[0]);
        string? component = jwt
            .Claims.FirstOrDefault(c => c.Type == AccessTokenClaimTypes.App)
            ?.Value;

        if (string.Equals(component, requirement.Component, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
