using System;
using System.IO;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models.Metrics;

/// <summary>
/// Summary information for a generated metrics file.
/// </summary>
public record MetricsSummary
{
    /// <summary>
    /// Gets or sets the stream containing the metrics file content.
    /// The caller is responsible for the lifecycle of the stream.
    /// </summary>
    [JsonPropertyName("fileStream")]
    public Stream FileStream { get; init; } = null!;

    /// <summary>
    /// Gets or sets the file name of the metrics file.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the hash of the file content (e.g. MD5) used for integrity checks.
    /// </summary>
    [JsonPropertyName("fileHash")]
    public string FileHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Gets or sets the total number of file transfers represented in the file.
    /// </summary>
    [JsonPropertyName("totalFileTransferCount")]
    public int TotalFileTransferCount { get; init; }

    /// <summary>
    /// Gets or sets the time when the metrics file was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }
}
