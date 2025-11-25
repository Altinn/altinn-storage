using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Azure.Storage.Queues;

using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients;

/// <summary>
/// Implementation of the <see cref="IFileScanQueueClient"/> using Azure Storage Queues.
/// </summary>
[ExcludeFromCodeCoverage]
public class FileScanQueueClient : IFileScanQueueClient
{
    private readonly QueueStorageSettings _queueStorageSettings;

    private QueueClient _fileScanQueueClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScanQueueClient"/> class.
    /// </summary>
    public FileScanQueueClient(IOptions<QueueStorageSettings> queueStorageSettings)
    {
        _queueStorageSettings = queueStorageSettings.Value;
    }

    /// <inheritdoc/>
    public async Task EnqueueFileScan(string content, CancellationToken ct)
    {
        QueueClient client = await GetFileScanQueueClient();
        await client.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), ct);
    }

    private async Task<QueueClient> GetFileScanQueueClient()
    {
        if (_fileScanQueueClient == null)
        {
            _fileScanQueueClient = new QueueClient(
                _queueStorageSettings.ConnectionString, 
                _queueStorageSettings.FileScanQueueName);

            await _fileScanQueueClient.CreateIfNotExistsAsync();
        }

        return _fileScanQueueClient;
    }
}