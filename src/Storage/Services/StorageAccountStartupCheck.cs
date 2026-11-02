using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Reads every storage account row when the application starts, so a row this build cannot read
/// stops the application from starting instead of failing the blob operations that need the row.
/// </summary>
/// <remarks>
/// Hosted services start after the database migrations in <c>Program.Configure</c> have run.
/// </remarks>
public sealed class StorageAccountStartupCheck(
    IStorageAccountRepository repository,
    ILogger<StorageAccountStartupCheck> logger
) : IHostedService
{
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StorageAccountRow> rows = await repository.ReadAll(cancellationToken);
        logger.LogInformation("Read {Count} storage account rows.", rows.Count);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
