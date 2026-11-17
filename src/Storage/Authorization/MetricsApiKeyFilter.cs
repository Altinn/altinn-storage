using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    : ApiKeyAuthorizationFilter(configuration, logger)
{
    /// <inheritdoc/>
    protected override string ConfigurationKey => "StorageMetricsApiKey";

    /// <inheritdoc/>
    protected override string EndpointName => "Metrics";
}
