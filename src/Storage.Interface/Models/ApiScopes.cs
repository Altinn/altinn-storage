using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// Custom scopes for an app
/// </summary>
public class ApiScopes 
{
    /// <summary>
    /// Gets or sets the read scope for the app
    /// </summary>
    [JsonProperty(PropertyName = "read")]
    public string Read { get; set; }

    /// <summary>
    /// Gets or sets the write scope for the app
    /// </summary>
    [JsonProperty(PropertyName = "write")]
    public string Write { get; set; }
}
