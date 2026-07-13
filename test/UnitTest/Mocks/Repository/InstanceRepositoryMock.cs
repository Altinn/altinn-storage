#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class InstanceRepositoryMock : IInstanceRepository
{
    private const long TestInstanceInternalId = 1;
    private static readonly Dictionary<string, StorageVersions> _versions = [];
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<InstanceInternal> Create(
        InstanceInternal instance,
        CancellationToken cancellationToken,
        int altinnMainVersion = 3
    )
    {
        Guid instanceGuid = Guid.NewGuid();

        InstanceInternal newInstance = new()
        {
            Id = instanceGuid.ToString(),
            AppId = instance.AppId,
            Org = instance.Org,
            InstanceOwner = instance.InstanceOwner,
            Process = instance.Process,
            Data = [],
            Versions = new StorageVersions(1, 1),
        };
        SetVersions(newInstance, new StorageVersions(1, 1));

        return await Task.FromResult(newInstance);
    }

    public Task<bool> Delete(Guid instanceGuid, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<InstanceQueryResult> GetInstancesFromQuery(
        InstanceQueryParameters queryParams,
        CancellationToken cancellationToken
    )
    {
        bool includeDataElements = queryParams.IncludeDataElements;
        List<InstanceInternal> instances = [];
        InstanceQueryResult response = new();

        string instancesPath = GetInstancesPath();

        if (Directory.Exists(instancesPath))
        {
            string[] files = Directory.GetFiles(
                instancesPath,
                "*.json",
                SearchOption.AllDirectories
            );

            foreach (var file in files)
            {
                string content = File.ReadAllText(file);
                InstanceInternal instance = JsonConvert.DeserializeObject<InstanceInternal>(
                    content
                );
                instance.Data = includeDataElements ? instance.Data ?? [] : [];
                PostProcess(instance);
                instances.Add(instance);
            }
        }

        if (!string.IsNullOrEmpty(queryParams.Org))
        {
            instances.RemoveAll(i =>
                !i.Org.Equals(queryParams.Org, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (!string.IsNullOrEmpty(queryParams.AppId))
        {
            instances.RemoveAll(i =>
                !i.AppId.Equals(queryParams.AppId, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (queryParams.InstanceOwnerPartyId.HasValue)
        {
            instances.RemoveAll(i =>
                queryParams.InstanceOwnerPartyId != Convert.ToInt32(i.InstanceOwner.PartyId)
            );
        }
        else if (
            queryParams.InstanceOwnerPartyIds != null
            && queryParams.InstanceOwnerPartyIds.Length > 0
        )
        {
            instances.RemoveAll(i =>
                !queryParams.InstanceOwnerPartyIds.Contains(
                    Convert.ToInt32(i.InstanceOwner.PartyId)
                )
            );
        }

        if (!string.IsNullOrEmpty(queryParams.ArchiveReference))
        {
            instances.RemoveAll(i => !i.Id.EndsWith(queryParams.ArchiveReference.ToLower()));
        }

        if (!string.IsNullOrEmpty(queryParams.DataValuesA2ArchRef))
        {
            instances.RemoveAll(i =>
                i.DataValues == null
                || !i.DataValues.TryGetValue("A2ArchRef", out string a2ArchRef)
                || !a2ArchRef.Equals(queryParams.DataValuesA2ArchRef, StringComparison.Ordinal)
            );
        }

        if (!string.IsNullOrEmpty(queryParams.A3Ref))
        {
            instances.RemoveAll(i =>
                i.Id == null
                || i.Id.Length < 12
                || !i.Id[^12..].Equals(queryParams.A3Ref, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (queryParams.IsArchived.HasValue)
        {
            instances.RemoveAll(i => i.Status.IsArchived != queryParams.IsArchived);
        }

        if (queryParams.IsHardDeleted.HasValue)
        {
            instances.RemoveAll(i => i.Status.IsHardDeleted != queryParams.IsHardDeleted);
        }

        if (queryParams.IsSoftDeleted.HasValue)
        {
            instances.RemoveAll(i => i.Status.IsSoftDeleted != queryParams.IsSoftDeleted);
        }

        instances.RemoveAll(i => i.Status.IsHardDeleted);

        response.Instances = instances;

        return Task.FromResult(response);
    }

    public Task<InstanceInternal> GetOne(
        Guid instanceGuid,
        bool includeElements,
        CancellationToken cancellationToken
    )
    {
        string instancePath = GetInstancePath(instanceGuid);
        if (File.Exists(instancePath))
        {
            string content = File.ReadAllText(instancePath);
            InstanceInternal instance = JsonConvert.DeserializeObject<InstanceInternal>(content);
            instance.Data = includeElements ? GetDataElements(instanceGuid) : [];
            PostProcess(instance);
            return Task.FromResult(instance);
        }

        return Task.FromResult<InstanceInternal>(null);
    }

    public Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        CancellationToken cancellationToken,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        if (instance.Id.Equals("d3b326de-2dd8-49a1-834a-b1d23b11e540"))
        {
            return Task.FromResult<InstanceInternal>(null);
        }

        ThrowIfVersionMismatch(instance, expectedInstanceVersion, expectedProcessStateVersion);
        StorageVersions current = GetVersions(instance);
        StorageVersions updated = new(
            current.InstanceVersion + 1,
            current.ProcessStateVersion
                + (updateProperties.Contains(nameof(instance.Process)) ? 1 : 0)
        );
        SetVersions(instance, updated);
        instance.Versions = updated;
        return Task.FromResult(instance);
    }

    public Task<InstanceInternal> UpdateReadStatus(
        InstanceInternal instanceInternal,
        CancellationToken cancellationToken
    )
    {
        StorageVersions versions = GetVersions(instanceInternal);
        instanceInternal.Versions = versions;
        return Task.FromResult(instanceInternal);
    }

    public Task<List<InstanceInternal>> GetHardDeletedInstances(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<List<DataElementInternal>> GetHardDeletedDataElements(
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    private static string GetInstancePath(Guid instanceGuid)
    {
        return Path.Combine(GetInstancesPath(), instanceGuid.ToString() + ".json");
    }

    private static List<DataElementInternal> GetDataElements(Guid instanceGuid)
    {
        List<DataElementInternal> dataElements = [];
        string dataElementsPath = GetDataElementsPath();

        string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
        foreach (string elementPath in dataElementPaths)
        {
            string content = File.ReadAllText(elementPath);
            DataElementInternal dataElement =
                System.Text.Json.JsonSerializer.Deserialize<DataElementInternal>(content, _options);
            if (dataElement.InstanceGuid.Contains(instanceGuid.ToString()))
            {
                dataElements.Add(dataElement);
            }
        }

        return dataElements;
    }

    private static string GetDataElementsPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(InstanceRepositoryMock).Assembly.Location).LocalPath
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

    private static string GetInstancesPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(InstanceRepositoryMock).Assembly.Location).LocalPath
        );
        return Path.Combine(unitTestFolder, "..", "..", "..", "data", "postgresdata", "instances");
    }

    private static void PostProcess(InstanceInternal instance)
    {
        instance.InternalId = TestInstanceInternalId;
        instance.Versions = GetVersions(instance);
        if (instance.Data != null && instance.Data.Count != 0)
        {
            SetReadStatus(instance);
        }

        (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
        instance.LastChanged = lastChanged;
        instance.LastChangedBy = lastChangedBy;
    }

    private static void SetReadStatus(InstanceInternal instance)
    {
        if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Exists(d => !d.IsRead))
        {
            instance.Status.ReadStatus = ReadStatus.UpdatedSinceLastReview;
        }
        else if (
            instance.Status.ReadStatus == ReadStatus.Read
            && !instance.Data.Exists(d => d.IsRead)
        )
        {
            instance.Status.ReadStatus = ReadStatus.Unread;
        }
    }

    private static void ThrowIfVersionMismatch(
        InstanceInternal instance,
        int? expectedInstanceVersion,
        int? expectedProcessStateVersion
    )
    {
        StorageVersions current = GetVersions(instance);
        if (
            expectedInstanceVersion is not null
            && expectedInstanceVersion != current.InstanceVersion
        )
        {
            throw new InstanceVersionMismatchException(
                current.InstanceVersion,
                current.ProcessStateVersion
            );
        }

        if (
            expectedProcessStateVersion is not null
            && expectedProcessStateVersion != current.ProcessStateVersion
        )
        {
            throw new ProcessStateVersionMismatchException(
                current.InstanceVersion,
                current.ProcessStateVersion
            );
        }
    }

    private static StorageVersions GetVersions(InstanceInternal instance)
    {
        string key = instance.Id ?? string.Empty;
        if (!_versions.TryGetValue(key, out StorageVersions versions))
        {
            versions = new StorageVersions(1, 1);
            _versions[key] = versions;
        }

        return versions;
    }

    private static void SetVersions(InstanceInternal instance, StorageVersions versions)
    {
        _versions[instance.Id ?? string.Empty] = versions;
    }
}
