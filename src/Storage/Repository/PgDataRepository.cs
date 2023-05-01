using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
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
    public class PgDataRepository : IDataRepository, IHostedService
    {
        private readonly string _insertSql = "insert into storage.dataelements(instanceInternalId, instanceGuid, alternateId, element) VALUES ($1, $2, $3, $4);";
        private readonly string _readAllSql = "select element from storage.dataelements where instanceGuid = $1;";
        private readonly string _readAllForMultipleSql = "select element from storage.dataelements where instanceGuid = any ($1);";
        private readonly string _readSql = "select element from storage.dataelements where alternateId = $1;";
        private readonly string _deleteSql = "delete from storage.dataelements where alternateId = $1;";
        private readonly string _updateSql = "update storage.dataelements set element = $2 where alternateId = $1;";

        private readonly string _connectionString;
        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ISasTokenProvider _sasTokenProvider;
        private readonly ILogger<PgDataRepository> _logger;
        private readonly JsonSerializerOptions _options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        /// <summary>
        /// Initializes a new instance of the <see cref="PgDataRepository"/> class.
        /// </summary>
        /// <param name="postgresSettings">DB params.</param>
        /// <param name="sasTokenProvider">A provider that can be asked for SAS tokens.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        public PgDataRepository(
            IOptions<PostgreSqlSettings> postgresSettings,
            ISasTokenProvider sasTokenProvider,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<PgDataRepository> logger)
        {
            _connectionString =
                string.Format(postgresSettings.Value.ConnectionString, postgresSettings.Value.StorageDbPwd);
            _storageConfiguration = storageConfiguration.Value;
            _sasTokenProvider = sasTokenProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId)
        {
            dataElement.Id ??= Guid.NewGuid().ToString();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_insertSql, conn)
            {
                Parameters =
                {
                    new() { Value = instanceInternalId, NpgsqlDbType = NpgsqlDbType.Bigint },
                    new() { Value = new Guid(dataElement.InstanceGuid), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = new Guid(dataElement.Id), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = JsonSerializer.Serialize(dataElement, _options), NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.PrepareAsync();
            await pgcom.ExecuteNonQueryAsync();

            return dataElement;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();
            await using NpgsqlCommand pgcom = new(_deleteSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(dataElement.Id), NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
            await pgcom.PrepareAsync();
            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementGuid)
        {
            List<DataElement> elements = new();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_readSql, conn)
            {
                Parameters =
                {
                    new() { Value = dataElementGuid, NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
            await pgcom.PrepareAsync();
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                return JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element"));
            }
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            List<DataElement> elements = new();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_readAllSql, conn)
            {
                Parameters =
                {
                    new() { Value = instanceGuid, NpgsqlDbType = NpgsqlDbType.Uuid },
                },
            };
            await pgcom.PrepareAsync();
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    elements.Add(JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element")));
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

            List<DataElement> elements = new();
            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            await using NpgsqlCommand pgcom = new(_readAllForMultipleSql, conn)
            {
                Parameters =
                {
                    new() { Value = instanceGuidsAsGuids ?? (object)DBNull.Value, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid },
                },
            };
            await pgcom.PrepareAsync();
            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    DataElement element = JsonSerializer.Deserialize<DataElement>(reader.GetFieldValue<string>("element"));
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

            await using NpgsqlConnection conn = new(_connectionString);
            await conn.OpenAsync();
            await using var transaction = await conn.BeginTransactionAsync();
            DataElement element = await Read(Guid.Empty, dataElementId);
            if (element == null)
            {
                throw new ArgumentException(nameof(dataElementId), "Element not found");
            }

            ////TODO Find a more elegant way to patch the json
            var elementDict = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(element));
            bool propsToUpdate = false;
            foreach (var entry in propertylist)
            {
                if (elementDict.ContainsKey(entry.Key))
                {
                    propsToUpdate = true;
                    elementDict[entry.Key] = entry.Value;
                }
            }

            if (!propsToUpdate)
            {
                return element;
            }

            string elementString = JsonSerializer.Serialize(elementDict, _options);
            await using NpgsqlCommand pgcom = new(_updateSql, conn)
            {
                Parameters =
                {
                    new() { Value = new Guid(element.Id), NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = elementString, NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return JsonSerializer.Deserialize<DataElement>(elementString);
        }

        /// <inheritdoc/>
        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
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
