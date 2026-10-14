#nullable disable

using System;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Represents a set of configuration options when communicating with the platform API.
/// Instances of this class is initialised with values from app settings. Some values can be overridden by environment variables.
/// </summary>
public class RegisterServiceSettings
{
    /// <summary>
    /// Gets or sets the url for the Register API endpoint, e.g. <c>https://platform.altinn.no/register/api/v1/</c>.
    /// </summary>
    public string ApiRegisterEndpoint { get; set; }

    /// <summary>
    /// Gets the url for the internal Register API (v2), resolved relative to <see cref="ApiRegisterEndpoint"/>,
    /// e.g. <c>https://platform.altinn.no/register/api/v2/internal/</c>.
    /// </summary>
    /// <returns>The base address of the internal Register API</returns>
    public Uri GetInternalApiEndpoint() =>
        new(new Uri(ApiRegisterEndpoint.TrimEnd('/') + "/"), "../v2/internal/");
}
