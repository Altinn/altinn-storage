using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Prepared data element metadata and its optional staged blob version.
/// </summary>
public sealed record DataElementCreation(DataElementInternal Element, Guid? BlobVersion);
