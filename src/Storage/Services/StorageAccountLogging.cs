using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Log messages shared by the storage account services.
/// </summary>
internal static class StorageAccountLogging
{
    /// <summary>
    /// Logs a critical error for a storage account row this build cannot read.
    /// </summary>
    public static void LogUnreadableStorageAccount(
        this ILogger logger,
        UnreadableStorageAccountRow row
    )
    {
        logger.LogCritical(
            "Storage account {StorageAccountId} with provider '{Provider}' for org {Org} number {StorageAccountNumber} (writable: {Writable}) cannot be read: {Error}. Blob operations in the account, and writes for the org and number while it is writable, fail until the row is fixed.",
            row.Id,
            row.Provider,
            row.Org,
            row.StorageAccountNumber,
            row.Writable,
            row.Error
        );
    }
}
