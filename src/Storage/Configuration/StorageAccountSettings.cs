namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Settings for resolving storage accounts, bound from the <c>StorageAccounts</c> section.
/// </summary>
public class StorageAccountSettings
{
    /// <summary>
    /// Gets or sets how long, in seconds, the storage account rows are cached. This bounds how long a
    /// new writable account takes to receive writes on every instance of the service.
    /// </summary>
    public int CacheSeconds { get; set; } = 60;
}
