using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// What the file scanner should do with a blob version it has a scan request for.
/// </summary>
/// <param name="Status">Whether the version awaits its scan, and if not, why.</param>
/// <param name="ReadUrl">
/// When <paramref name="Status"/> is <see cref="FileScanTargetStatus.Pending"/>, a URL that reads the blob version
/// and nothing else until <paramref name="ExpiresAt"/>. It grants access to anyone who holds it, so it is never
/// logged.
/// </param>
/// <param name="ExpiresAt">When <paramref name="ReadUrl"/> stops working.</param>
public sealed record FileScanTarget(
    FileScanTargetStatus Status,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] Uri? ReadUrl = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        DateTimeOffset? ExpiresAt = null
);

/// <summary>
/// Whether a blob version awaits its file scan, and if not, why.
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum FileScanTargetStatus
{
    /// <summary>
    /// The version is the data element's current version and its scan is pending.
    /// </summary>
    Pending,

    /// <summary>
    /// A newer blob version has replaced this one; the newer version's scan reports for the data element.
    /// </summary>
    Superseded,

    /// <summary>
    /// The version is current, but its file scan result is no longer pending.
    /// </summary>
    NotPending,

    /// <summary>
    /// The data element no longer exists.
    /// </summary>
    Deleted,
}
