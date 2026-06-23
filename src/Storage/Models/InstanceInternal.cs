using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal instance metadata with storage-only data element fields.
/// </summary>
public sealed record InstanceInternal(
    Instance Instance,
    IReadOnlyList<DataElementInternal> DataElements
);
