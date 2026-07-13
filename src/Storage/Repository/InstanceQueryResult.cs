#nullable enable

using System.Collections.Generic;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents the storage-domain result of an instance query.
/// </summary>
public sealed class InstanceQueryResult
{
    /// <summary>
    /// Gets or sets the token for continuing the query, or <see langword="null"/> at the end.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the repository error message, if the query failed.
    /// </summary>
    public string? Exception { get; set; }

    /// <summary>
    /// Gets or sets the fully hydrated storage-domain instances.
    /// </summary>
    public List<InstanceInternal> Instances { get; set; } = [];
}
