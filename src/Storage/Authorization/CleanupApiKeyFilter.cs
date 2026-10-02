using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Authorization filter that validates an API key supplied in the <c>X-API-Key</c> header
/// for the cleanup endpoints. These endpoints perform irreversible deletions and are intended
/// for operational use from within the cluster, so they are guarded by a shared secret rather
/// than by an Altinn token.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CleanupApiKeyFilter"/> class.
/// </remarks>
/// <param name="configuration">The application configuration.</param>
/// <param name="logger">The logger.</param>
public class CleanupApiKeyFilter(IConfiguration configuration, ILogger<CleanupApiKeyFilter> logger)
    : ApiKeyAuthorizationFilter(configuration, logger)
{
    /// <inheritdoc/>
    protected override string ConfigurationKey => "StorageCleanupApiKey";

    /// <inheritdoc/>
    protected override string EndpointName => "Cleanup";
}
