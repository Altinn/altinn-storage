using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to data blobs and their metadata documents.
    /// </summary>
    public class DataService : IDataService
    {
        private readonly IFileScanQueueClient _fileScanQueueClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataService"/> class.
        /// </summary>
        public DataService(IFileScanQueueClient fileScanQueueClient)
        {
            _fileScanQueueClient = fileScanQueueClient;
        }

        /// <inheritdoc/>
        public async Task StartFileScan(DataType dataType, DataElement dataElement, CancellationToken ct)
        {
            if (dataType.EnableFileScan)
            {
                string serialisedDataElement = JsonSerializer.Serialize(
                    dataElement, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await _fileScanQueueClient.EnqueueFileScan(serialisedDataElement, ct);
            }
        }
    }
}
