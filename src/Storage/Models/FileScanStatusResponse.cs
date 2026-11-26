using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The response when the file scanner sets the file scan result of a data element.
/// </summary>
/// <param name="Status">The outcome.</param>
public sealed record FileScanStatusResponse(FileScanStatusOutcome Status);

/// <summary>
/// The outcome when the file scanner sets the file scan result of a data element.
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum FileScanStatusOutcome
{
    /// <summary>
    /// Storage set the file scan result.
    /// </summary>
    Updated,

    /// <summary>
    /// Storage did not set the result, because a newer blob version replaced the scanned version. The scan of the
    /// newer version sets the result.
    /// </summary>
    Superseded,

    /// <summary>
    /// Storage did not set the result, because the data element or its instance does not exist.
    /// </summary>
    Deleted,
}
