using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal data element delete prepared by the controller.
/// </summary>
public sealed record DataElementDeletion(Guid DataElementId, bool IgnoreLock = false);
