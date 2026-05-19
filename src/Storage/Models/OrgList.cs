using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Defines a list of organisations
/// </summary>
/// <param name="orgs">Dictionary of orgs</param>
public class OrgList
{
    /// <summary>
    /// Dictionary of orgs
    /// </summary>
    public Dictionary<string, Org>? orgs { get; set; }
};
