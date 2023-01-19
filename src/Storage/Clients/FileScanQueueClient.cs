using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Azure.Storage.Queues;

using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// Implementation of the <see cref="IFileScanQueueClient"/> using Azure Storage Queues.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class FileScanQueueClient : IFileScanQueueClient
    {
        private readonly FileScanQueueSettings _fileScanQueueSettings;

        private QueueClient _fileScanQueueClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileScanQueueClient"/> class.
        /// </summary>
        public FileScanQueueClient(IOptions<FileScanQueueSettings> fileScanQueueSettings)
        {
            _fileScanQueueSettings = fileScanQueueSettings.Value;
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
                    _fileScanQueueSettings.ConnectionString, 
                    _fileScanQueueSettings.FileScanQueueName);

                await _fileScanQueueClient.CreateIfNotExistsAsync();
            }

            return _fileScanQueueClient;
        }
    }
}
