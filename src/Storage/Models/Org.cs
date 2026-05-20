using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Describes an organisation
/// </summary>
public class Org
{
    /// <summary>
    /// Name of organisation. With language support
    /// </summary>
    [JsonPropertyName("name")]
    public Dictionary<string, string>? Name { get; set; }

    /// <summary>
    /// The logo
    /// </summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    /// <summary>
    /// The organisation number
    /// </summary>
    [JsonPropertyName("orgnr")]
    public string? Orgnr { get; set; }

    /// <summary>
    /// The homepage
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// The environments available for the organzation
    /// </summary>
    [JsonPropertyName("enviorments")]
    public List<string> Environments { get; set; } = [];
}
