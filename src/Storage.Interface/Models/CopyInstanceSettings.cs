#nullable disable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// A class holding copy instance settings.
/// </summary>
public class CopyInstanceSettings
{
    /// <summary>
    /// Gets or sets a boolean indicating if copy instance is enabled.
    /// </summary>
    [JsonProperty(PropertyName = "enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a list of excluded data types.
    /// </summary>
    [JsonProperty(PropertyName = "excludedDataTypes")]
    public List<string> ExcludedDataTypes { get; set; }

    /// <summary>
    /// Gets or sets a list of excluded datafields.
    /// </summary>
    [JsonProperty(PropertyName = "excludedDataFields")]
    public List<string> ExcludedDataFields { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if copying of attachments is enabled.
    /// </summary>
    [JsonProperty(PropertyName = "includeAttachments")]
    public bool IncludeAttachments { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if the due date (dueBefore) should be copied from the source instance.
    /// </summary>
    [JsonProperty(PropertyName = "includeDueBefore")]
    public bool IncludeDueBefore { get; set; }

    /// <summary>
    /// Gets or sets the keys of the data values that should be copied from the source instance.
    /// </summary>
    [JsonProperty(PropertyName = "includedDataValues")]
    public List<string> IncludedDataValues { get; set; }

    /// <summary>
    /// Gets or sets the keys of the presentation texts that should be copied from the source instance.
    /// </summary>
    [JsonProperty(PropertyName = "includedPresentationTexts")]
    public List<string> IncludedPresentationTexts { get; set; }
}
