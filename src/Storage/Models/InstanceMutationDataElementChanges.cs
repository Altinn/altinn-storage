using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Explicit data element changes; omitted properties retain their stored values.
/// </summary>
public sealed record InstanceMutationDataElementChanges
{
    private static readonly InstanceMutationDataElementChanges _empty = new();

    /// <summary>
    /// Gets the locked flag change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Locked { get; init; }

    /// <summary>
    /// Gets the read flag change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsRead { get; init; }

    /// <summary>
    /// Gets the content size change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; init; }

    /// <summary>
    /// Gets the content type change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<string?> ContentType { get; init; }

    /// <summary>
    /// Gets the filename change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<string?> Filename { get; init; }

    /// <summary>
    /// Gets the blob path change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<string?> BlobStoragePath { get; init; }

    /// <summary>
    /// Gets the data element refs replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<IReadOnlyList<Guid>?> Refs { get; init; }

    /// <summary>
    /// Gets the references replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<IReadOnlyList<Reference>?> References { get; init; }

    /// <summary>
    /// Gets the tags replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<IReadOnlyList<string>?> Tags { get; init; }

    /// <summary>
    /// Gets the application metadata replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<IReadOnlyList<KeyValueEntry>?> Metadata { get; init; }

    /// <summary>
    /// Gets the user metadata replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<IReadOnlyList<KeyValueEntry>?> UserDefinedMetadata { get; init; }

    /// <summary>
    /// Gets the delete status replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Change<DeleteStatus?> DeleteStatus { get; init; }

    /// <summary>
    /// Gets the file scan result replacement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileScanResult? FileScanResult { get; init; }

    /// <summary>
    /// Gets whether all data element changes are omitted.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => this == _empty;
}
