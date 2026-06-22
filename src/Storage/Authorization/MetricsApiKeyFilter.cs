using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Authorization filter that validates an API key supplied in the <c>X-API-Key</c> header
/// for the metrics endpoints. The key is forwarded by API Management, which validates the
/// caller's <c>Ocp-Apim-Subscription-Key</c> before proxying the request to this service.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MetricsApiKeyFilter"/> class.
/// </remarks>
/// <param name="configuration">The application configuration.</param>
/// <param name="logger">The logger.</param>
public class MetricsApiKeyFilter(IConfiguration configuration, ILogger<MetricsApiKeyFilter> logger)
    : IAsyncAuthorizationFilter
{
    /// <summary>
    /// Validates the API key for metrics endpoints.
    /// </summary>
    /// <param name="context">The authorization filter context.</param>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Only apply to metrics endpoints.
        if (
            !context.HttpContext.Request.Path.StartsWithSegments(
                "/storage/api/v1/metrics/instances"
            )
        )
        {
            return; // Not a Metrics endpoint, let it pass.
        }

        // Check if API key is provided.
        if (
            !context.HttpContext.Request.Headers.TryGetValue(
                "X-API-Key",
                out StringValues apiKeyHeader
            )
        )
        {
            logger.LogWarning(
                "Metrics endpoint accessed without API key from IP: {ClientIp}",
                GetClientIpAddress(context.HttpContext)
            );
            context.Result = new UnauthorizedObjectResult(
                new { error = "API key required for metrics endpoints" }
            );
            return;
        }

        string? providedApiKey = apiKeyHeader.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            logger.LogWarning(
                "Metrics endpoint accessed with empty API key from IP: {ClientIp}",
                GetClientIpAddress(context.HttpContext)
            );
            context.Result = new UnauthorizedObjectResult(
                new { error = "API key cannot be empty" }
            );
            return;
        }

        // Get configured API key.
        string? configuredApiKey = configuration["StorageMetricsApiKey"];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            logger.LogError("StorageMetricsApiKey is not configured in application settings");
            context.Result = new UnauthorizedObjectResult(
                new { error = "API key validation not configured" }
            );
            return;
        }

        // Validate API key using constant-time comparison.
        if (!SecureEquals(providedApiKey, configuredApiKey))
        {
            logger.LogWarning(
                "Metrics endpoint accessed with invalid API key from IP: {ClientIp}",
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
