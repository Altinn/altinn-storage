#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
    private readonly object _stateLock = new();
    private readonly Dictionary<string, StoredDataElement> _tempRepository = new();
    private readonly Dictionary<string, List<BlobVersionEntry>> _blobVersions = new();
    private int _instanceVersion = 1;
    private int _processStateVersion = 1;
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public Task<DataElementWriteResult> Create(
        DataElementInternal dataElement,
        long instanceInternalId = 0,
        CancellationToken cancellationToken = default,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        string dataElementId = string.IsNullOrEmpty(dataElement.Id)
            ? Guid.NewGuid().ToString()
            : dataElement.Id;
        Guid instanceGuid = new(dataElement.InstanceGuid);
        _ = new Guid(dataElementId);
        string blobVersionId = ValidateBlobVersionId(dataElement.BlobVersionId);
        DataElementInternal stagedElement = CloneDataElement(dataElement);
        stagedElement.Id = dataElementId;
        stagedElement.BlobVersionId = blobVersionId;

        StorageVersions versions;
        lock (_stateLock)
        {
            ThrowIfVersionMismatchLocked(expectedInstanceVersion, expectedProcessStateVersion);
            if (_tempRepository.ContainsKey(dataElementId))
            {
                throw new ArgumentException(
                    $"An item with the same key has already been added. Key: {dataElementId}"
                );
            }

            BlobVersionEntry blobVersion = FindBlobVersionToAttachLocked(
                dataElementId,
                blobVersionId,
                instanceGuid
            );
            if (blobVersionId is not null && blobVersion is null)
            {
                throw new RepositoryException(
                    $"Blob version {dataElement.BlobVersionId} is not available for data element {dataElementId}.",
                    HttpStatusCode.Conflict
                );
            }

            string serializedDataElement = JsonSerializer.Serialize(stagedElement, _options);
            if (blobVersion is not null)
            {
                blobVersion.Attached = true;
            }

            _tempRepository.Add(
                dataElementId,
                new StoredDataElement(serializedDataElement, blobVersionId, instanceGuid)
            );
            versions = BumpInstanceVersionLocked();
        }

        dataElement.Id = dataElementId;
        dataElement.BlobVersionId = blobVersionId;
        return Task.FromResult(new DataElementWriteResult(dataElement, versions));
    }

    public Task<bool> DeleteForCleanup(
        DataElementInternal dataElement,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(true);

    public Task<DataElementInternal> Read(
        Guid instanceGuid,
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        EnsureDataElementLoaded(dataElementId);
        DataElementInternal dataElement;
        lock (_stateLock)
        {
            dataElement = _tempRepository.TryGetValue(
                dataElementId.ToString(),
                out StoredDataElement storedDataElement
            )
                ? DeserializeStoredDataElement(storedDataElement)
                : null;
        }

        return Task.FromResult(dataElement);
    }

    public Task<DataElementWriteResult> Update(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertylist,
        DataElementUpdateContext context = null,
        CancellationToken cancellationToken = default
    )
    {
        const int allowedNumberOfProperties = 16;
        if (propertylist.Count > allowedNumberOfProperties)
        {
            throw new ArgumentOutOfRangeException(
                nameof(propertylist),
                $"PropertyList can contain at most {allowedNumberOfProperties} entries."
            );
        }

        context ??= new DataElementUpdateContext();
        string expectedBlobVersionId = ValidateBlobVersionId(context.ExpectedCurrentBlobVersion);
        string dataElementKey = dataElementId.ToString();
        EnsureDataElementLoaded(dataElementId);

        DataElementInternal stagedElement;
        StorageVersions versions;
        lock (_stateLock)
        {
            ThrowIfVersionMismatchLocked(
                context.ExpectedInstanceVersion,
                context.ExpectedProcessStateVersion
            );
            if (
                !_tempRepository.TryGetValue(
                    dataElementKey,
                    out StoredDataElement storedDataElement
                )
                || storedDataElement.InstanceGuid != instanceGuid
            )
            {
                throw DataElementNotFound(dataElementId);
            }

            if (
                expectedBlobVersionId is not null
                && !string.Equals(
                    expectedBlobVersionId,
                    storedDataElement.CurrentBlobVersion,
                    StringComparison.Ordinal
                )
            )
            {
                throw new DataElementBlobVersionMismatchException(
                    $"Data element {dataElementId} current blob version did not match expected version.",
                    _instanceVersion,
                    _processStateVersion
                );
            }

            stagedElement = DeserializeStoredDataElement(storedDataElement);
            string requestedBlobVersionId = ApplyUpdates(stagedElement, propertylist);
            string normalizedBlobVersionId = ValidateBlobVersionId(requestedBlobVersionId);
            string currentBlobVersion =
                normalizedBlobVersionId ?? storedDataElement.CurrentBlobVersion;
            stagedElement.BlobVersionId = currentBlobVersion;

            BlobVersionEntry blobVersion = FindBlobVersionToAttachLocked(
                dataElementKey,
                normalizedBlobVersionId,
                instanceGuid
            );
            if (normalizedBlobVersionId is not null && blobVersion is null)
            {
                throw new RepositoryException(
                    $"Blob version was not available for data element {dataElementId}.",
                    HttpStatusCode.Conflict
                );
            }

            string serializedDataElement = JsonSerializer.Serialize(stagedElement, _options);
            if (blobVersion is not null)
            {
                blobVersion.Attached = true;
            }

            _tempRepository[dataElementKey] = new StoredDataElement(
                serializedDataElement,
                currentBlobVersion,
                storedDataElement.InstanceGuid
            );
            versions = BumpInstanceVersionLocked();
        }

        return Task.FromResult(new DataElementWriteResult(stagedElement, versions));
    }

    public Task<DataElementWriteResult> UpdateReadStatus(
        Guid instanceGuid,
        Guid dataElementId,
        bool isRead,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            UpdateStoredDataElement(instanceGuid, dataElementId, element => element.IsRead = isRead)
        );

    public Task<DataElementWriteResult> UpdateLockStatus(
        Guid instanceGuid,
        Guid dataElementId,
        bool locked,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            UpdateStoredDataElement(instanceGuid, dataElementId, element => element.Locked = locked)
        );

    public Task<DataElementWriteResult?> UpdateFileScanStatus(
        Guid instanceGuid,
        Guid dataElementId,
        FileScanStatus fileScanStatus,
        CancellationToken cancellationToken = default
    )
    {
        string expectedBlobVersionId = ValidateBlobVersionId(fileScanStatus.BlobVersionId);
        string dataElementKey = dataElementId.ToString();
        EnsureDataElementLoaded(dataElementId);

        DataElementInternal stagedElement;
        StorageVersions versions;
        lock (_stateLock)
        {
            if (
                !_tempRepository.TryGetValue(
                    dataElementKey,
                    out StoredDataElement storedDataElement
                )
                || storedDataElement.InstanceGuid != instanceGuid
                || (
                    expectedBlobVersionId is not null
                    && !string.Equals(
                        expectedBlobVersionId,
                        storedDataElement.CurrentBlobVersion,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return Task.FromResult<DataElementWriteResult?>(null);
            }

            stagedElement = DeserializeStoredDataElement(storedDataElement);
            stagedElement.FileScanResult = fileScanStatus.FileScanResult;
            string serializedDataElement = JsonSerializer.Serialize(stagedElement, _options);
            _tempRepository[dataElementKey] = new StoredDataElement(
                serializedDataElement,
                storedDataElement.CurrentBlobVersion,
                storedDataElement.InstanceGuid
            );
            versions = CurrentStorageVersionsLocked();
        }

        return Task.FromResult<DataElementWriteResult?>(
            new DataElementWriteResult(stagedElement, versions)
        );
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
        lock (_stateLock)
        {
            AddBlobVersionLocked(
                dataElementId.ToString(),
                new BlobVersionEntry(
                    blobVersionId,
                    instanceGuid,
                    appId,
                    blobStorageOrg,
                    storageAccountNumber
                )
            );
        }

        return Task.FromResult(blobVersionId);
    }

    public Task<bool> DeleteBlobVersion(
        Guid dataElementId,
        string blobVersionId,
        CancellationToken cancellationToken = default
    )
    {
        string normalizedBlobVersionId = ValidateBlobVersionId(blobVersionId);
        if (normalizedBlobVersionId is null)
        {
            return Task.FromResult(false);
        }

        lock (_stateLock)
        {
            bool deleted = RemoveDetachedBlobVersionLocked(
                dataElementId.ToString(),
                normalizedBlobVersionId
            );
            return Task.FromResult(deleted);
        }
    }

    public Task<int> DeleteBlobVersions(
        Guid dataElementId,
        IReadOnlyList<string> blobVersionIds,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<string> normalizedBlobVersionIds = NormalizeBlobVersionIds(blobVersionIds);
        string dataElementKey = dataElementId.ToString();
        lock (_stateLock)
        {
            int deleteCount = 0;
            if (_blobVersions.TryGetValue(dataElementKey, out List<BlobVersionEntry> versions))
            {
                deleteCount = versions.RemoveAll(version =>
                    !version.Attached && normalizedBlobVersionIds.Contains(version.BlobVersionId)
                );
                if (versions.Count == 0)
                {
                    _blobVersions.Remove(dataElementKey);
                }
            }

            return Task.FromResult(deleteCount);
        }
    }

    public Task<int> DeleteOrphanBlobVersions(
        IReadOnlyList<string> blobVersionIds,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<string> normalizedBlobVersionIds = NormalizeBlobVersionIds(blobVersionIds);
        lock (_stateLock)
        {
            int deleteCount = 0;
            foreach (
                (string dataElementId, List<BlobVersionEntry> versions) in _blobVersions.ToArray()
            )
            {
                deleteCount += versions.RemoveAll(version =>
                    !version.Attached && normalizedBlobVersionIds.Contains(version.BlobVersionId)
                );
                if (versions.Count == 0)
                {
                    _blobVersions.Remove(dataElementId);
                }
            }

            return Task.FromResult(deleteCount);
        }
    }

    public Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    ) => ReadBlobVersionsByAttachment(dataElementId, true);

    private Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadBlobVersionsByAttachment(
        Guid dataElementId,
        bool attached
    )
    {
        BlobVersionEntry[] snapshot;
        lock (_stateLock)
        {
            snapshot = _blobVersions.TryGetValue(
                dataElementId.ToString(),
                out List<BlobVersionEntry> versions
            )
                ? versions.Where(version => version.Attached == attached).ToArray()
                : [];
        }

        IReadOnlyList<BlobVersionReferencesInternal> blobVersions =
        [
            .. snapshot
                .GroupBy(version =>
                    (
                        version.InstanceGuid,
                        version.AppId,
                        version.BlobStorageOrg,
                        version.StorageAccountNumber
                    )
                )
                .Select(group => new BlobVersionReferencesInternal(
                    group.Key.InstanceGuid,
                    group.Key.AppId,
                    group.Key.BlobStorageOrg,
                    group.Key.StorageAccountNumber,
                    group.Select(version => version.BlobVersionId).ToArray()
                )),
        ];

        return Task.FromResult(blobVersions);
    }

    public Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadDetachedBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    ) => ReadBlobVersionsByAttachment(dataElementId, false);

    public Task<bool> Exists(Guid dataElementId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

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

    private static string ValidateBlobVersionId(string blobVersionId)
    {
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return null;
        }

        try
        {
            return BlobVersionId.Encode(BlobVersionId.Decode(blobVersionId));
        }
        catch (FormatException exception)
        {
            throw new RepositoryException(
                $"Blob version id '{blobVersionId}' is not valid.",
                exception,
                HttpStatusCode.BadRequest
            );
        }
    }

    private static HashSet<string> NormalizeBlobVersionIds(IReadOnlyList<string> blobVersionIds) =>
        blobVersionIds
            ?.Where(blobVersionId => !string.IsNullOrEmpty(blobVersionId))
            .Select(blobVersionId => BlobVersionId.Encode(BlobVersionId.Decode(blobVersionId)))
            .ToHashSet(StringComparer.Ordinal)
        ?? [];

    private static DataElementInternal CloneDataElement(DataElementInternal dataElement) =>
        JsonSerializer.Deserialize<DataElementInternal>(
            JsonSerializer.Serialize(dataElement, _options),
            _options
        );

    private static DataElementInternal DeserializeStoredDataElement(
        StoredDataElement storedDataElement
    )
    {
        DataElementInternal dataElement = JsonSerializer.Deserialize<DataElementInternal>(
            storedDataElement.SerializedDataElement,
            _options
        );
        dataElement.BlobVersionId = storedDataElement.CurrentBlobVersion;
        return dataElement;
    }

    private static string ApplyUpdates(
        DataElementInternal dataElement,
        Dictionary<string, object> propertylist
    )
    {
        string requestedBlobVersionId = null;
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
                requestedBlobVersionId = (string)entry.Value;
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

            if (entry.Key == "/isRead")
            {
                dataElement.IsRead = (bool)entry.Value;
            }
        }

        return requestedBlobVersionId;
    }

    private BlobVersionEntry FindBlobVersionToAttachLocked(
        string dataElementId,
        string blobVersionId,
        Guid instanceGuid
    )
    {
        if (
            blobVersionId is null
            || !_blobVersions.TryGetValue(dataElementId, out List<BlobVersionEntry> versions)
        )
        {
            return null;
        }

        return versions.Find(version =>
            !version.Attached
            && version.InstanceGuid == instanceGuid
            && string.Equals(version.BlobVersionId, blobVersionId, StringComparison.Ordinal)
        );
    }

    private void AddBlobVersionLocked(string dataElementId, BlobVersionEntry blobVersion)
    {
        if (string.IsNullOrEmpty(dataElementId))
        {
            return;
        }

        if (!_blobVersions.TryGetValue(dataElementId, out List<BlobVersionEntry> versions))
        {
            versions = [];
            _blobVersions[dataElementId] = versions;
        }

        if (
            !versions.Exists(version =>
                string.Equals(
                    version.BlobVersionId,
                    blobVersion.BlobVersionId,
                    StringComparison.Ordinal
                )
            )
        )
        {
            versions.Add(blobVersion);
        }
    }

    private bool RemoveDetachedBlobVersionLocked(string dataElementId, string blobVersionId)
    {
        if (!_blobVersions.TryGetValue(dataElementId, out List<BlobVersionEntry> versions))
        {
            return false;
        }

        int versionIndex = versions.FindIndex(version =>
            !version.Attached
            && string.Equals(version.BlobVersionId, blobVersionId, StringComparison.Ordinal)
        );
        if (versionIndex < 0)
        {
            return false;
        }

        versions.RemoveAt(versionIndex);
        if (versions.Count == 0)
        {
            _blobVersions.Remove(dataElementId);
        }

        return true;
    }

    private void EnsureDataElementLoaded(Guid dataElementId)
    {
        string dataElementKey = dataElementId.ToString();
        lock (_stateLock)
        {
            if (_tempRepository.ContainsKey(dataElementKey))
            {
                return;
            }
        }

        DataElementInternal dataElement = ReadDataElementFile(dataElementId);
        if (dataElement is null)
        {
            return;
        }

        string currentBlobVersion = ValidateBlobVersionId(dataElement.BlobVersionId);
        dataElement.BlobVersionId = currentBlobVersion;
        Guid instanceGuid = new(dataElement.InstanceGuid);
        string serializedDataElement = JsonSerializer.Serialize(dataElement, _options);
        lock (_stateLock)
        {
            _tempRepository.TryAdd(
                dataElementKey,
                new StoredDataElement(serializedDataElement, currentBlobVersion, instanceGuid)
            );
        }
    }

    private static DataElementInternal ReadDataElementFile(Guid dataElementId)
    {
        lock (TestDataUtil.DataLock)
        {
            string elementPath = Path.Combine(
                GetDataElementsPath(),
                dataElementId.ToString() + ".json"
            );
            if (File.Exists(elementPath))
            {
                string content = File.ReadAllText(elementPath);
                return JsonSerializer.Deserialize<DataElementInternal>(content, _options);
            }
        }

        return null;
    }

    private DataElementWriteResult UpdateStoredDataElement(
        Guid instanceGuid,
        Guid dataElementId,
        Action<DataElementInternal> update
    )
    {
        EnsureDataElementLoaded(dataElementId);
        string dataElementKey = dataElementId.ToString();
        lock (_stateLock)
        {
            if (
                !_tempRepository.TryGetValue(
                    dataElementKey,
                    out StoredDataElement storedDataElement
                )
                || storedDataElement.InstanceGuid != instanceGuid
            )
            {
                throw DataElementNotFound(dataElementId);
            }

            DataElementInternal stagedElement = DeserializeStoredDataElement(storedDataElement);
            update(stagedElement);
            string serializedDataElement = JsonSerializer.Serialize(stagedElement, _options);
            _tempRepository[dataElementKey] = new StoredDataElement(
                serializedDataElement,
                storedDataElement.CurrentBlobVersion,
                storedDataElement.InstanceGuid
            );
            return new DataElementWriteResult(stagedElement, CurrentStorageVersionsLocked());
        }
    }

    private void ThrowIfVersionMismatchLocked(
        int? expectedInstanceVersion,
        int? expectedProcessStateVersion
    )
    {
        if (expectedInstanceVersion is not null && expectedInstanceVersion != _instanceVersion)
        {
            throw new InstanceVersionMismatchException(_instanceVersion, _processStateVersion);
        }

        if (
            expectedProcessStateVersion is not null
            && expectedProcessStateVersion != _processStateVersion
        )
        {
            throw new ProcessStateVersionMismatchException(_instanceVersion, _processStateVersion);
        }
    }

    private StorageVersions BumpInstanceVersionLocked()
    {
        _instanceVersion++;
        return CurrentStorageVersionsLocked();
    }

    private StorageVersions CurrentStorageVersionsLocked() =>
        new(_instanceVersion, _processStateVersion);

    private static RepositoryException DataElementNotFound(Guid dataElementId) =>
        new($"Data element {dataElementId} was not found.", HttpStatusCode.NotFound);

    private sealed record StoredDataElement(
        string SerializedDataElement,
        string CurrentBlobVersion,
        Guid InstanceGuid
    );

    private sealed record BlobVersionEntry(
        string BlobVersionId,
        Guid InstanceGuid,
        string AppId,
        string BlobStorageOrg,
        int? StorageAccountNumber
    )
    {
        public bool Attached { get; set; }
    }
}
