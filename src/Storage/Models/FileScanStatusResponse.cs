using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The response to setting the file scan status of a data element.
/// </summary>
/// <param name="Status">Whether the status was set, and if not, why.</param>
public sealed record FileScanStatusResponse(FileScanStatusOutcome Status);

/// <summary>
/// Whether a file scan status was set on a data element, and if not, why.
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum FileScanStatusOutcome
{
    /// <summary>
    /// The file scan status was set.
    /// </summary>
    Updated,

    /// <summary>
    /// The scanned blob version is no longer the data element's current version, so the status was not set.
    /// The current version's own scan reports for the data element.
    /// </summary>
    Superseded,

    /// <summary>
    /// The data element or its instance no longer exists.
    /// </summary>
    Deleted,
}
