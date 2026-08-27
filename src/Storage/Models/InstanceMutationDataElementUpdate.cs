#nullable disable

using System;
using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal data element update prepared by the controller after blob staging.
/// </summary>
public sealed record InstanceMutationDataElementUpdate(
    Guid DataElementId,
    Dictionary<string, object> Properties,
    string ExpectedCurrentBlobVersion,
    bool IgnoreLock = false
);
