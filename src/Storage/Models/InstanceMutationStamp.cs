using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The shared timestamp and actor applied by Storage to a committed mutation.
/// </summary>
public sealed record InstanceMutationStamp(DateTime LastChanged, string? LastChangedBy);
