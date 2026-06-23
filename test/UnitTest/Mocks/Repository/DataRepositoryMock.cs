#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class DataRepositoryMock : IDataRepository
{
    private readonly Dictionary<string, string> _tempRepository;
    private readonly Dictionary<string, List<string>> _blobVersions;
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public DataRepositoryMock()
    {
        _tempRepository = new();
        _blobVersions = new();
    }

    public async Task<DataElementInternal> Create(
        DataElementInternal dataElement,
        long instanceInternalId,
        CancellationToken cancellationToken = default
    )
    {
        _tempRepository.Add(
            dataElement.DataElement.Id,
            JsonSerializer.Serialize(dataElement.DataElement, _options)
        );
        AddBlobVersion(dataElement.DataElement.Id, dataElement.BlobVersionId);
        return await Task.FromResult(dataElement);
    }

    public async Task<bool> Delete(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    ) => await Task.FromResult(true);

    public async Task<bool> DeleteForCleanup(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    ) => await Task.FromResult(true);

    public async Task<DataElementInternal> Read(
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

            return await Task.FromResult(
                new DataElementInternal(dataElement, GetLatestBlobVersion(dataElement.Id))
            );
        }

        return null;
    }

    public async Task<DataElement> Update(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertylist,
        DataElementUpdateContext context = null,
        CancellationToken cancellationToken = default
    )
    {
        context ??= new DataElementUpdateContext();
        DataElement dataElement = null;
        if (_tempRepository.TryGetValue(dataElementId.ToString(), out string serializedDataElement))
        {
            dataElement = JsonSerializer.Deserialize<DataElement>(serializedDataElement, _options);
        }
        else
        {
            dataElement = (await Read(instanceGuid, dataElementId, cancellationToken))?.DataElement;
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

            if (entry.Key == "/currentBlobVersion")
            {
                AddBlobVersion(dataElementId.ToString(), (string)entry.Value);
            }

            if (entry.Key == "/blobStoragePath")
            {
                dataElement.BlobStoragePath = (string)entry.Value;
            }

            if (entry.Key == "/deleteStatus")
            {
                dataElement.DeleteStatus = (DeleteStatus)entry.Value;
            }

            if (entry.Key == "/lastChanged")
            {
                dataElement.LastChanged = (DateTime?)entry.Value;
            }

            if (entry.Key == "/lastChangedBy")
            {
                dataElement.LastChangedBy = (string)entry.Value;
            }
        }

        _tempRepository[dataElementId.ToString()] = JsonSerializer.Serialize(dataElement, _options);

        return dataElement;
    }

    public async Task<DataElement> UpdateFileScanStatus(
        Guid instanceGuid,
        Guid dataElementId,
        FileScanStatus fileScanStatus,
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
            dataElement = (await Read(instanceGuid, dataElementId, cancellationToken))?.DataElement;
        }

        if (dataElement == null)
        {
            throw new RepositoryException(
                "Data element not found",
                System.Net.HttpStatusCode.NotFound
            );
        }

        if (
            !string.IsNullOrEmpty(fileScanStatus.BlobVersionId)
            && !string.Equals(
                fileScanStatus.BlobVersionId,
                GetLatestBlobVersion(dataElement.Id),
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        dataElement.FileScanResult = fileScanStatus.FileScanResult;
        _tempRepository[dataElement.Id] = JsonSerializer.Serialize(dataElement, _options);

        return dataElement;
    }

    public Task<string> CreateBlobVersionId(
        Guid instanceGuid,
        Guid dataElementId,
        string appId,
        string blobStorageOrg,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        AddBlobVersion(dataElementId.ToString(), blobVersionId);
        return Task.FromResult(blobVersionId);
    }

    public Task<bool> DeleteBlobVersion(
        Guid dataElementId,
        string blobVersionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_blobVersions.TryGetValue(dataElementId.ToString(), out List<string> versions))
        {
            versions.RemoveAll(version =>
                string.Equals(version, blobVersionId, StringComparison.Ordinal)
            );
        }

        return Task.FromResult(true);
    }

    public Task<int> DeleteOrphanBlobVersions(
        IReadOnlyList<string> blobVersionIds,
        CancellationToken cancellationToken = default
    )
    {
        int deleteCount = 0;
        if (blobVersionIds is not null)
        {
            foreach (List<string> versions in _blobVersions.Values)
            {
                deleteCount += versions.RemoveAll(version => blobVersionIds.Contains(version));
            }
        }

        return Task.FromResult(deleteCount);
    }

    public Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            _blobVersions.TryGetValue(
                dataElementId.ToString(),
                out List<string> dataElementBlobVersions
            )
            && dataElementBlobVersions.Count > 0
        )
        {
            DataElement dataElement = null;
            if (
                _tempRepository.TryGetValue(
                    dataElementId.ToString(),
                    out string serializedDataElement
                )
            )
            {
                dataElement = JsonSerializer.Deserialize<DataElement>(
                    serializedDataElement,
                    _options
                );
            }

            string appId = null;
            if (!string.IsNullOrEmpty(dataElement?.BlobStoragePath))
            {
                string marker = $"/{dataElement.InstanceGuid}/data-elements/";
                int markerIndex = dataElement.BlobStoragePath.IndexOf(
                    marker,
                    StringComparison.Ordinal
                );
                if (markerIndex > 0)
                {
                    appId = dataElement.BlobStoragePath[..markerIndex];
                }
            }
            string blobStorageOrg = appId?.Split('/')[0];
            IReadOnlyList<BlobVersionReferencesInternal> blobVersions =
            [
                new BlobVersionReferencesInternal(
                    string.IsNullOrEmpty(dataElement?.InstanceGuid)
                        ? Guid.Empty
                        : Guid.Parse(dataElement.InstanceGuid),
                    appId ?? string.Empty,
                    blobStorageOrg ?? string.Empty,
                    null,
                    [.. dataElementBlobVersions]
                ),
            ];

            return Task.FromResult(blobVersions);
        }

        return Task.FromResult<IReadOnlyList<BlobVersionReferencesInternal>>([]);
    }

    public async Task<bool> Exists(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        return await Task.FromResult(true);
    }

    public Task<bool> DeleteForInstance(
        string instanceId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(true);

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

    private void AddBlobVersion(string dataElementId, string blobVersionId)
    {
        if (string.IsNullOrEmpty(dataElementId) || string.IsNullOrEmpty(blobVersionId))
        {
            return;
        }

        if (!_blobVersions.TryGetValue(dataElementId, out List<string> versions))
        {
            versions = [];
            _blobVersions[dataElementId] = versions;
        }

        if (
            !versions.Exists(version =>
                string.Equals(version, blobVersionId, StringComparison.Ordinal)
            )
        )
        {
            versions.Add(blobVersionId);
        }
    }

    private string GetLatestBlobVersion(string dataElementId)
    {
        return
            _blobVersions.TryGetValue(dataElementId, out List<string> versions)
            && versions.Count > 0
            ? versions[^1]
            : null;
    }
}
