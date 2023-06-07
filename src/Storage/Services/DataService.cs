using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to data blobs and their metadata documents.
    /// </summary>
    public class DataService : IDataService
    {
        private readonly IFileScanQueueClient _fileScanQueueClient;
        private readonly IDataRepository _dataRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataService"/> class.
        /// </summary>
        public DataService(IFileScanQueueClient fileScanQueueClient, IDataRepository dataRepository)
        {
            _fileScanQueueClient = fileScanQueueClient;
            _dataRepository = dataRepository;
        }

        /// <inheritdoc/>
        public async Task StartFileScan(Instance instance, DataType dataType, DataElement dataElement, DateTimeOffset blobTimestamp, CancellationToken ct)
        {
            if (dataType.EnableFileScan)
            {
                FileScanRequest fileScanRequest = new()
                {
                    InstanceId = instance.Id,
                    DataElementId = dataElement.Id,
                    Timestamp = blobTimestamp,
                    BlobStoragePath = dataElement.BlobStoragePath,
                    Filename = dataElement.Filename,
                    Org = instance.Org
                };

                string serialisedRequest = JsonSerializer.Serialize(
                    fileScanRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                await _fileScanQueueClient.EnqueueFileScan(serialisedRequest, ct);
            }
        }

        /// <inheritdoc/>
        public async Task<string> GenerateSha256Hash(string org, Guid instanceGuid, Guid dataElementId)
        {
            DataElement dataElement = await _dataRepository.Read(instanceGuid, dataElementId);

            // if dataelement is null what to do

            Stream filestream = await _dataRepository.ReadDataFromStorage(org, dataElement.BlobStoragePath);

            // if blob not exists (stream = null) or empty stream...

            return CalculateSha256Hash(filestream);
        }

        private string CalculateSha256Hash(Stream fileStream)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(fileStream);
                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}
