using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IDataRepository"/>.
    /// </summary>
    public class PgDataRepository : IDataRepository
    {
        private readonly string _insertSql = "call storage.insertdataelement ($1, $2, $3, $4)";
        private readonly string _readAllSql = "select * from storage.readalldataelement($1)";
        private readonly string _readAllForMultipleSql = "select * from storage.readallformultipledataelement($1)";
        private readonly string _readSql = "select * from storage.readdataelement($1)";
        private readonly string _deleteSql = "call storage.deletedataelement ($1)";
        private readonly string _updateSql = "call storage.updatedataelement ($1, $2)";

        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ISasTokenProvider _sasTokenProvider;
        private readonly ILogger<PgDataRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgDataRepository"/> class.
        /// </summary>
        /// <param name="sasTokenProvider">A provider that can be asked for SAS tokens.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgDataRepository(
            ISasTokenProvider sasTokenProvider,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<PgDataRepository> logger,
            NpgsqlDataSource dataSource)
        {
            _storageConfiguration = storageConfiguration.Value;
            _sasTokenProvider = sasTokenProvider;
            _logger = logger;
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0)
        {
            if (dataElement.DataType == "signature")
            {
                _logger.LogError("DebugPg2Postgres0 " + dataElement.Id);
            }

            try
            {
                dataElement.Id ??= Guid.NewGuid().ToString();
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, dataElement);

                await pgcom.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("DebugPg2Postgres-e " + dataElement.Id + " " + ex.Message);
                throw;
            }

            if (dataElement.DataType == "signature")
            {
                _logger.LogError("DebugPg2Postgres1 " + dataElement.Id);
            }

            return dataElement;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));

            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            await reader.ReadAsync();
            return reader.GetFieldValue<DataElement>("element");
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            List<DataElement> elements = new();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readAllSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    elements.Add(reader.GetFieldValue<DataElement>("element"));
                }
            }

            return elements;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids)
        {
            ////TODO: Remove this method/interface and join the dataelements at the inestance level
            //// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            Dictionary<string, List<DataElement>> dataElements = new();
            if (instanceGuids == null || instanceGuids.Count == 0)
            {
                return dataElements;
            }

            foreach (var guidString in instanceGuids)
            {
                dataElements[guidString] = new List<DataElement>();
            }

            List<Guid> instanceGuidsAsGuids = new();
            foreach (var instance in instanceGuids)
            {
                instanceGuidsAsGuids.Add(new Guid(instance));
            }

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readAllForMultipleSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Uuid, instanceGuidsAsGuids ?? (object)DBNull.Value);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    DataElement element = reader.GetFieldValue<DataElement>("element");
                    if (!dataElements.ContainsKey(element.InstanceGuid))
                    {
                        dataElements.Add(element.InstanceGuid, new List<DataElement>());
                    }

                    dataElements[element.InstanceGuid].Add(element);
                }
            }

            return dataElements;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist)
        {
            if (propertylist.Count > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(propertylist), "PropertyList can contain at most 10 entries.");
            }

            await using var transConnection = _dataSource.CreateConnection();
            await transConnection.OpenAsync();
            await using var transaction = await transConnection.BeginTransactionAsync(isolationLevel: IsolationLevel.RepeatableRead); // Ensure that the read element is locked until updated
            DataElement element = await Read(Guid.Empty, dataElementId) ?? throw new ArgumentException("Element not found for id " + dataElementId, nameof(dataElementId));

            foreach (var kvp in propertylist)
            {
                switch (kvp.Key)
                {
                    case "/locked": element.Locked = (bool)kvp.Value; break;
                    case "/refs": element.Refs = (List<Guid>)kvp.Value; break;
                    case "/references": element.References = (List<Reference>)kvp.Value; break;
                    case "/tags": element.Tags = (List<string>)kvp.Value; break;
                    case "/deleteStatus": element.DeleteStatus = (DeleteStatus)kvp.Value; break;
                    case "/lastChanged": element.LastChanged = (DateTime?)kvp.Value; break;
                    case "/lastChangedBy": element.LastChangedBy = (string)kvp.Value; break;
                    case "/fileScanResult": element.FileScanResult = (FileScanResult)kvp.Value; break;
                    case "/contentType": element.ContentType = (string)kvp.Value; break;
                    case "/filename": element.Filename = (string)kvp.Value; break;
                    case "/size": element.Size = (long)kvp.Value; break;
                    default: throw new ArgumentException("Unexpected key " + kvp.Key);
                }
            }

            await using NpgsqlCommand pgcom = new(_updateSql, transConnection)
            {
                Parameters =
                {
                    new() { Value = dataElementId, NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = element, NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return element;
        }

        // Blob stuff below ---------------------------------------------------------------------------------------------------

        /// <inheritdoc/>
        public async Task<Stream> ReadDataFromStorage(string org, string blobStoragePath)
        {
            try
            {
                return await DownloadBlobAsync(org, blobStoragePath);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token and retrying download operation.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        return await DownloadBlobAsync(org, blobStoragePath);
                    case "BlobNotFound":
                        _logger.LogWarning("Unable to find a blob based on the given information - {org}: {blobStoragePath}", org, blobStoragePath);

                        // Returning null because the blob does not exist.
                        return null;
                    case "InvalidRange":
                        _logger.LogWarning("Found possibly empty blob in storage for {org}: {blobStoragePath}", org, blobStoragePath);

                        // Returning empty stream because the blob does exist, but it is empty.
                        return new MemoryStream();
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteDataToStorage(string org, Stream stream, string blobStoragePath)
        {
            try
            {
                var blobProps = await UploadFromStreamAsync(org, stream, blobStoragePath);
                return (blobProps.ContentLength, blobProps.LastModified);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        // No use retrying upload as the original stream can't be reset back to start.
                        throw;
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDataInStorage(string org, string blobStoragePath)
        {
            try
            {
                return await DeleteIfExistsAsync(org, blobStoragePath);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token and retrying delete operation.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        return await DeleteIfExistsAsync(org, blobStoragePath);
                    default:
                        throw;
                }
            }
        }

        private async Task<BlobProperties> UploadFromStreamAsync(string org, Stream stream, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);
            BlobUploadOptions options = new()
            {
                TransferValidation = new UploadTransferValidationOptions { ChecksumAlgorithm = StorageChecksumAlgorithm.MD5 }
            };
            await blockBlob.UploadAsync(stream, options);
            BlobProperties properties = await blockBlob.GetPropertiesAsync();

            return properties;
        }

        private async Task<Stream> DownloadBlobAsync(string org, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);

            Azure.Response<BlobDownloadInfo> response = await blockBlob.DownloadAsync();

            return response.Value.Content;
        }

        private async Task<bool> DeleteIfExistsAsync(string org, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);

            bool result = await blockBlob.DeleteIfExistsAsync();

            return result;
        }

        private async Task<BlobClient> CreateBlobClient(string org, string blobName)
        {
            if (!_storageConfiguration.AccountName.StartsWith("devstoreaccount1"))
            {
                string sasToken = await _sasTokenProvider.GetSasToken(org);

                string accountName = string.Format(_storageConfiguration.OrgStorageAccount, org);
                string containerName = string.Format(_storageConfiguration.OrgStorageContainer, org);

                UriBuilder fullUri = new()
                {
                    Scheme = "https",
                    Host = $"{accountName}.blob.core.windows.net",
                    Path = $"{containerName}/{blobName}",
                    Query = sasToken
                };

                return new BlobClient(fullUri.Uri);
            }

            StorageSharedKeyCredential storageCredentials = new(_storageConfiguration.AccountName, _storageConfiguration.AccountKey);
            Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
            BlobServiceClient commonBlobClient = new(storageUrl, storageCredentials);
            BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(_storageConfiguration.StorageContainer);

            return blobContainerClient.GetBlobClient(blobName);
        }
    }
}
