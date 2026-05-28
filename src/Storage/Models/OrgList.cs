using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Defines a list of organisations
/// </summary>
public class OrgList
{
    /// <summary>
    /// Dictionary of orgs
    /// </summary>
    [JsonPropertyName("orgs")]
    public Dictionary<string, Org>? Orgs { get; set; }
};
