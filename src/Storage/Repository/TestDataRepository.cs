using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
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
    /// Represents an implementation of <see cref="TestDataRepository"/>.
    /// </summary>
    public class TestDataRepository : ITestDataRepository
    {
        private readonly IDataRepository _cosmosRepository;
        private readonly IDataRepository _postgresRepository;
        private readonly ILogger<PgDataRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestDataRepository"/> class.
        /// </summary>
        /// <param name="sasTokenProvider">A provider that can be asked for SAS tokens.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="cosmosRepository">The cosmos repository.</param>
        public TestDataRepository(
            ISasTokenProvider sasTokenProvider,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<PgDataRepository> logger,
            NpgsqlDataSource dataSource,
            IDataRepository cosmosRepository)
        {
            _postgresRepository = new PgDataRepository(sasTokenProvider, storageConfiguration, logger, dataSource);
            _cosmosRepository = cosmosRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0)
        {
            DataElement cosmosElement = await _cosmosRepository.Create(dataElement, instanceInternalId);
            DataElement postgresElement = await _postgresRepository.Create(dataElement, instanceInternalId);
            return cosmosElement;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            bool cosmosDelete = await _cosmosRepository.Delete(dataElement);
            bool postgresDelete = await _postgresRepository.Delete(dataElement);
            if (cosmosDelete != postgresDelete)
            {
                _logger.LogError("TestPgData: Diff in Delete for item " + dataElement.Id);
                throw new Exception("Diff in Delete for item " + dataElement.Id);
            }

            return cosmosDelete;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId)
        {
            DataElement cosmosRead = await _cosmosRepository.Read(instanceGuid, dataElementId);
            DataElement postgresRead = await _postgresRepository.Read(instanceGuid, dataElementId);

            if (!CompareElements(cosmosRead, postgresRead))
            {
                _logger.LogError($"TestPgData: Diff in Read postgres data: {JsonSerializer.Serialize(postgresRead, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgData: Diff in Read cosmos data: {JsonSerializer.Serialize(cosmosRead, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgData: Diff in Read for {instanceGuid} {dataElementId}");
                throw new Exception($"Diff in Read for {instanceGuid} {dataElementId}");
            }

            return cosmosRead;
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            List<DataElement> cosmosRead = await _cosmosRepository.ReadAll(instanceGuid);
            List<DataElement> postgresRead = await _postgresRepository.ReadAll(instanceGuid);

            string postgresJson = JsonSerializer.Serialize(postgresRead);
            string cosmosJson = JsonSerializer.Serialize(cosmosRead);
            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgData: Diff in ReadAll postgres data: {JsonSerializer.Serialize(postgresRead, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgData: Diff in ReadAll cosmos data: {JsonSerializer.Serialize(cosmosRead, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgData: Diff in ReadAll for {instanceGuid}");
                throw new Exception($"Diff in ReadAll for {instanceGuid}");
            }

            return cosmosRead;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids)
        {
            Dictionary<string, List<DataElement>> cosmosRead = await _cosmosRepository.ReadAllForMultiple(instanceGuids);
            Dictionary<string, List<DataElement>> postgresRead = await _postgresRepository.ReadAllForMultiple(instanceGuids);

            string postgresJson = JsonSerializer.Serialize(postgresRead);
            string cosmosJson = JsonSerializer.Serialize(cosmosRead);
            if (cosmosJson != postgresJson)
            {
                string guids = null;
                foreach (string guid in instanceGuids)
                {
                    guids += guid + ", ";
                }

                _logger.LogError($"TestPgData: Diff in ReadAllForMultiple postgres data: {JsonSerializer.Serialize(postgresRead, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgData: Diff in ReadAllForMultiple cosmos data: {JsonSerializer.Serialize(cosmosRead, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgData: Diff in ReadAllForMultiple for {guids}");
                throw new Exception($"Diff in ReadAllForMultiple for {guids}");
            }

            return cosmosRead;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist)
        {
            DataElement cosmosUpdate = await _cosmosRepository.Update(instanceGuid, dataElementId, propertylist);
            DataElement postgresUpdate = await _postgresRepository.Update(instanceGuid, dataElementId, propertylist);

            if (!CompareElements(cosmosUpdate, postgresUpdate))
            {
                string props = null;
                foreach (var property in propertylist)
                {
                    props += $"{property.Key}:{property.Value}, ";
                }

                _logger.LogError($"TestPgData: Diff in Update postgres data: {JsonSerializer.Serialize(postgresUpdate, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgData: Diff in Update cosmos data: {JsonSerializer.Serialize(cosmosUpdate, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgData: Diff in Update for {instanceGuid} {dataElementId} {props}");
                throw new Exception($"Diff in Update for {instanceGuid} {dataElementId} {props}");
            }

            return cosmosUpdate;
        }

        /// <inheritdoc/>
        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteDataToStorage(string org, Stream stream, string blobStoragePath)
        {
            return await _postgresRepository.WriteDataToStorage(org, stream, blobStoragePath);
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadDataFromStorage(string org, string blobStoragePath)
        {
            return await _postgresRepository.ReadDataFromStorage(org, blobStoragePath);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDataInStorage(string org, string blobStoragePath)
        {
            return await _postgresRepository.DeleteDataInStorage(org, blobStoragePath);
        }

        private bool CompareElements (DataElement cosmosElement, DataElement postgresElement)
        {
            string postgresJson = JsonSerializer.Serialize(postgresElement);
            string cosmosJson = JsonSerializer.Serialize(cosmosElement);
            if (cosmosJson != postgresJson)
            {
                if (TestInstanceRepository.IgnoreFileScan)
                {
                    DataElement patchedCosmos = JsonSerializer.Deserialize<DataElement>(cosmosJson);
                    DataElement patchedPostgres = JsonSerializer.Deserialize<DataElement>(postgresJson);
                    patchedCosmos.FileScanResult = FileScanResult.Clean;
                    patchedPostgres.FileScanResult = FileScanResult.Clean;
                    postgresJson = JsonSerializer.Serialize(patchedPostgres);
                    cosmosJson = JsonSerializer.Serialize(patchedCosmos);
                }

                if (cosmosJson != postgresJson)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
