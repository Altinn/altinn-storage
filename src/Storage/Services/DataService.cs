using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

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
        public async Task StartFileScan(Instance instance, DataType dataType, DataElement dataElement, CancellationToken ct)
        {
            if (dataType.EnableFileScan)
            {
                FileScanRequest fileScanRequest = new()
                {
                    InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
                    DataElementId = dataElement.Id,
                    BlobStoragePath= dataElement.BlobStoragePath,
                    Filename= dataElement.Filename,
                    Org = instance.Org
                };

                string serialisedRequest = JsonSerializer.Serialize(fileScanRequest);
                await _fileScanQueueClient.EnqueueFileScan(serialisedRequest, ct);
            }
        }
    }
}
