using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Reads every storage account row when the application starts and logs a critical error for each row
/// this build cannot read. Such a row does not stop the application: only the blob operations that use
/// the row fail.
/// </summary>
/// <remarks>
/// Hosted services start after the database migrations in <c>Program.Configure</c> have run. A failure to
/// read the table itself still stops the application from starting.
/// </remarks>
public sealed class StorageAccountStartupCheck(
    IStorageAccountRepository repository,
    ILogger<StorageAccountStartupCheck> logger
) : IHostedService
{
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        StorageAccountRows rows = await repository.ReadAll(cancellationToken);
        foreach (UnreadableStorageAccountRow row in rows.Unreadable)
        {
            logger.LogUnreadableStorageAccount(row);
        }

        logger.LogInformation(
            "Read {Count} storage account rows, {UnreadableCount} unreadable.",
            rows.Readable.Count + rows.Unreadable.Count,
            rows.Unreadable.Count
        );
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
