using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository
{
    public class DataRepositoryMock : IDataRepository
    {
        private readonly Dictionary<string, string> _tempRepository;
        private readonly JsonSerializerOptions _options;

        public DataRepositoryMock()
        {
            _tempRepository = new();
            _options = new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        public async Task<DataElement> Create(DataElement dataElement)
        {
            _tempRepository.Add(dataElement.Id, JsonSerializer.Serialize(dataElement, _options));
            return await Task.FromResult(dataElement);
        }

        public async Task<bool> Delete(DataElement dataElement)
        {
            return await Task.FromResult(true);
        }

        public async Task<bool> DeleteDataInStorage(string org, string blobStoragePath)
        {
            return await Task.FromResult(true);
        }

        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId)
        {
            DataElement dataElement = null;

            lock (TestDataUtil.DataLock)
            {
                string elementPath = Path.Combine(GetDataElementsPath(), dataElementId.ToString() + ".json");
                if (File.Exists(elementPath))
                {
                    string content = File.ReadAllText(elementPath);
                    dataElement = JsonSerializer.Deserialize<DataElement>(content, _options);
                }
            }

            if (dataElement != null)
            {
                _tempRepository[dataElement.Id] = JsonSerializer.Serialize(dataElement, _options);

                return await Task.FromResult(dataElement);
            }

            return null;
        }

        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            List<DataElement> dataElements = new List<DataElement>();
            string dataElementsPath = GetDataElementsPath();

            string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
            foreach (string elementPath in dataElementPaths)
            {
                string content = File.ReadAllText(elementPath);
                DataElement dataElement = JsonSerializer.Deserialize<DataElement>(content, _options);
                if (dataElement.InstanceGuid.Contains(instanceGuid.ToString()))
                {
                    dataElements.Add(dataElement);
                }
            }

            return await Task.FromResult(dataElements);
        }

        public async Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids)
        {
            Dictionary<string, List<DataElement>> dataElements = new();
            foreach (var instanceGuid in instanceGuids)
            {
                dataElements[instanceGuid] = new List<DataElement>();
            }

            string dataElementsPath = GetDataElementsPath();

            string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
            foreach (string elementPath in dataElementPaths)
            {
                string content = File.ReadAllText(elementPath);
                DataElement dataElement = JsonSerializer.Deserialize<DataElement>(content, _options);
                if (instanceGuids.Contains(dataElement.InstanceGuid))
                {
                    dataElements[dataElement.InstanceGuid].Add(dataElement);
                }
            }

            return await Task.FromResult(dataElements);
        }

        public async Task<Stream> ReadDataFromStorage(string org, string blobStoragePath)
        {
            string dataPath = Path.Combine(GetDataBlobPath(), blobStoragePath);
            Stream fs = File.OpenRead(dataPath);

            return await Task.FromResult(fs);
        }

        public Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertyList)
        {
            _tempRepository.TryGetValue(dataElementId.ToString(), out string serializedDataElement);

            DataElement dataElement = JsonSerializer.Deserialize<DataElement>(serializedDataElement, _options);
            if (dataElement == null)
            {
                return null;
            }

            foreach (var entry in propertyList)
            {
                if (entry.Key == "/fileScanResult")
                {
                    dataElement.FileScanResult = (FileScanResult)entry.Value;
                }

                if (entry.Key == "/locked")
                {
                    dataElement.Locked = (bool)entry.Value;
                }
            }

            _tempRepository["dataElementId"] = JsonSerializer.Serialize(dataElement, _options);

            return Task.FromResult(dataElement);
        }

        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteDataToStorage(string org, Stream stream, string blobStoragePath)
        {
            MemoryStream memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return (memoryStream.Length, DateTimeOffset.UtcNow);
        }

        private static string GetDataElementsPath()
        {
            string unitTestFolder = Path.GetDirectoryName(new Uri(typeof(DataRepositoryMock).Assembly.Location).LocalPath);
            return Path.Combine(unitTestFolder, "..", "..", "..", "data", "cosmoscollections", "dataelements");
        }

        private static string GetDataBlobPath()
        {
            string unitTestFolder = Path.GetDirectoryName(new Uri(typeof(DataRepositoryMock).Assembly.Location).LocalPath);
            return Path.Combine(unitTestFolder, "..", "..", "..", "data", "blob");
        }
    }
}
