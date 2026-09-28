#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Enums;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Metadata used when creating a data element and its first blob version.
/// </summary>
public sealed record DataElementCreateOptions
{
    /// <summary>
    /// Gets the data element id.
    /// </summary>
    public required Guid DataElementId { get; init; }

    /// <summary>
    /// Gets the data type id.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Gets the blob content type.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Gets the filename to expose in data element metadata.
    /// </summary>
    public string Filename { get; init; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public required DateTime Created { get; init; }

    /// <summary>
    /// Gets the user or org that creates the data element.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// Gets optional data element refs.
    /// </summary>
    public List<Guid> Refs { get; init; }

    /// <summary>
    /// Gets an optional task id this data element was generated from.
    /// </summary>
    public string GeneratedFromTask { get; init; }

    /// <summary>
    /// Gets the file scan result to store initially.
    /// </summary>
    public FileScanResult FileScanResult { get; init; } = FileScanResult.NotApplicable;

    /// <summary>
    /// Gets a value indicating whether the data element is locked.
    /// </summary>
    public bool Locked { get; init; }

    /// <summary>
    /// Gets a value indicating whether the data element has been read.
    /// </summary>
    public bool IsRead { get; init; } = true;
}
