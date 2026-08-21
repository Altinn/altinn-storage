namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Current storage-owned versions for an instance.
/// </summary>
public sealed record StorageVersions(int InstanceVersion, int ProcessStateVersion);
