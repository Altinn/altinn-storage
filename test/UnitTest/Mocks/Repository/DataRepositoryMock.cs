using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class DataRepositoryMock : IDataRepository
{
    private readonly Dictionary<string, string> _tempRepository;
    private readonly JsonSerializerOptions _options;

    public DataRepositoryMock()
    {
        _tempRepository = new();
        _options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    }

    public async Task<DataElement> Create(
        DataElement dataElement,
        long instanceInternalId = 0,
        CancellationToken cancellationToken = default
    )
    {
        _tempRepository.Add(dataElement.Id, JsonSerializer.Serialize(dataElement, _options));
        return await Task.FromResult(dataElement);
    }

    public async Task<bool> Delete(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    ) => await Task.FromResult(true);

    public async Task<DataElement> Read(
        Guid instanceGuid,
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        DataElement dataElement = null;

        lock (TestDataUtil.DataLock)
        {
            string elementPath = Path.Combine(
                GetDataElementsPath(),
                dataElementId.ToString() + ".json"
            );
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

    public async Task<DataElement> Update(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertylist,
        CancellationToken cancellationToken = default
    )
    {
        DataElement dataElement = null;
        if (_tempRepository.TryGetValue(dataElementId.ToString(), out string serializedDataElement))
        {
            dataElement = JsonSerializer.Deserialize<DataElement>(serializedDataElement, _options);
        }
        else
        {
            dataElement = await Read(instanceGuid, dataElementId, cancellationToken);
        }

        if (dataElement == null)
        {
            throw new RepositoryException(
                "Data element not found",
                System.Net.HttpStatusCode.NotFound
            );
        }

        foreach (var entry in propertylist)
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

        return dataElement;
    }

    public Task<bool> DeleteForInstance(
        string instanceId,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    private static string GetDataElementsPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(DataRepositoryMock).Assembly.Location).LocalPath
        );
        return Path.Combine(
            unitTestFolder,
            "..",
            "..",
            "..",
            "data",
            "postgresdata",
            "dataelements"
        );
    }
}
