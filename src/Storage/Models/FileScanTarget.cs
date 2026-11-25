using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The scan state of a blob version, with a URL to read it when the scan is pending.
/// </summary>
/// <param name="Status">The scan state of the blob version.</param>
/// <param name="ReadUrl">
/// A read-only URL for the blob version. Set only for <see cref="FileScanTargetStatus.Pending"/>. Each person or
/// system that has the URL can read the blob, so do not write the URL to logs.
/// </param>
/// <param name="ExpiresAt">The time after which <paramref name="ReadUrl"/> does not work.</param>
public sealed record FileScanTarget(
    FileScanTargetStatus Status,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] Uri? ReadUrl = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        DateTimeOffset? ExpiresAt = null
);

/// <summary>
/// The scan state of a blob version.
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum FileScanTargetStatus
{
    /// <summary>
    /// The blob version is the current version of the data element, and its scan is pending. Scan it.
    /// </summary>
    Pending,

    /// <summary>
    /// A newer blob version replaced this version. Do not scan it. The scan of the newer version sets the result.
    /// </summary>
    Superseded,

    /// <summary>
    /// The blob version is the current version, but it already has a file scan result. Do not scan it.
    /// </summary>
    NotPending,

    /// <summary>
    /// The data element does not exist. Do not scan it.
    /// </summary>
    Deleted,
}
