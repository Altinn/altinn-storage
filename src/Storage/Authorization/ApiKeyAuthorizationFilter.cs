using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Base class for authorization filters that guard an endpoint with a shared secret supplied
/// in the <c>X-API-Key</c> header. The expected key is read from configuration, where it is
/// populated from Key Vault, so a deployment without the secret fails closed with
/// <see cref="StatusCodes.Status503ServiceUnavailable"/> rather than allowing the request.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApiKeyAuthorizationFilter"/> class.
/// </remarks>
/// <param name="configuration">The application configuration.</param>
/// <param name="logger">The logger, supplied by the derived type so log categories stay per-filter.</param>
public abstract class ApiKeyAuthorizationFilter(IConfiguration configuration, ILogger logger)
    : IAuthorizationFilter
{
    /// <summary>
    /// The configuration key holding the expected API key.
    /// </summary>
    protected abstract string ConfigurationKey { get; }

    /// <summary>
    /// Name of the endpoint group being guarded, used in log and error messages.
    /// </summary>
    protected abstract string EndpointName { get; }

    /// <summary>
    /// Validates the API key for the guarded endpoint.
    /// </summary>
    /// <param name="context">The authorization filter context.</param>
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Check if API key is provided.
        if (
            !context.HttpContext.Request.Headers.TryGetValue(
                "X-API-Key",
                out StringValues apiKeyHeader
            )
        )
        {
            logger.LogWarning(
                "{EndpointName} endpoint accessed without API key from IP: {ClientIp}",
                EndpointName,
                GetClientIpAddress(context.HttpContext)
            );
            context.Result = new UnauthorizedObjectResult(
                new { error = $"API key required for {EndpointName} endpoints" }
            );
            return;
        }

        string? providedApiKey = apiKeyHeader.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            logger.LogWarning(
                "{EndpointName} endpoint accessed with empty API key from IP: {ClientIp}",
                EndpointName,
                GetClientIpAddress(context.HttpContext)
            );
            context.Result = new UnauthorizedObjectResult(
                new { error = "API key cannot be empty" }
            );
            return;
        }

        // Get configured API key.
        string? configuredApiKey = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            logger.LogError(
                "{ConfigurationKey} is not configured in application settings",
                ConfigurationKey
            );
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        // Validate API key using constant-time comparison.
        if (!SecureEquals(providedApiKey, configuredApiKey))
        {
            logger.LogWarning(
                "{EndpointName} endpoint accessed with invalid API key from IP: {ClientIp}",
                EndpointName,
                GetClientIpAddress(context.HttpContext)
            );
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid API key" });
        }
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP first (in case of proxy/load balancer).
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedFor))
        {
            return forwardedFor.FirstOrDefault()?.Split(',')[0].Trim();
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out StringValues realIp))
        {
            return realIp.FirstOrDefault();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool SecureEquals(string a, string b)
    {
        byte[] abytes = Encoding.UTF8.GetBytes(a);
        byte[] bbytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(abytes, bbytes);
    }
}
