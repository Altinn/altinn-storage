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
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class InstanceRepositoryMock : IInstanceRepository
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<Instance> Create(
        Instance instance,
        CancellationToken cancellationToken,
        int altinnMainVersion = 3
    )
    {
        string partyId = instance.InstanceOwner.PartyId;
        Guid instanceGuid = Guid.NewGuid();

        Instance newInstance = new Instance
        {
            Id = $"{partyId}/{instanceGuid}",
            AppId = instance.AppId,
            Org = instance.Org,
            InstanceOwner = instance.InstanceOwner,
            Process = instance.Process,
            Data = new List<DataElement>(),
        };

        return await Task.FromResult(newInstance);
    }

    public Task<bool> Delete(Instance instance, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<InstanceQueryResponse> GetInstancesFromQuery(
        InstanceQueryParameters queryParams,
        bool includeDataelements,
        CancellationToken cancellationToken
    )
    {
        List<Instance> instances = [];
        InstanceQueryResponse response = new();

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
                Instance instance = (Instance)
                    JsonConvert.DeserializeObject(content, typeof(Instance));
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
        response.Count = instances.Count;

        return Task.FromResult(response);
    }

    public Task<(Instance Instance, long InternalId)> GetOne(
        Guid instanceGuid,
        bool includeElements,
        CancellationToken cancellationToken
    )
    {
        string instancePath = GetInstancePath(instanceGuid);
        if (File.Exists(instancePath))
        {
            string content = File.ReadAllText(instancePath);
            Instance instance = (Instance)JsonConvert.DeserializeObject(content, typeof(Instance));
            instance.Data = includeElements ? GetDataElements(instanceGuid) : null;
            PostProcess(instance);
            return Task.FromResult<(Instance, long)>((instance, 0));
        }

        return Task.FromResult<(Instance, long)>((null, 0));
    }

    public Task<Instance> Update(
        Instance instance,
        List<string> updateProperties,
        CancellationToken cancellationToken
    )
    {
        if (instance.Id.Equals("1337/d3b326de-2dd8-49a1-834a-b1d23b11e540"))
        {
            return Task.FromResult<Instance>(null);
        }

        instance.Data = new List<DataElement>();

        return Task.FromResult(instance);
    }

    public Task<List<Instance>> GetHardDeletedInstances(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<List<DataElement>> GetHardDeletedDataElements(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private static string GetInstancePath(Guid instanceGuid)
    {
        return Path.Combine(GetInstancesPath(), instanceGuid.ToString() + ".json");
    }

    private static List<DataElement> GetDataElements(Guid instanceGuid)
    {
        List<DataElement> dataElements = new List<DataElement>();
        string dataElementsPath = GetDataElementsPath();

        string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
        foreach (string elementPath in dataElementPaths)
        {
            string content = File.ReadAllText(elementPath);
            DataElement dataElement = System.Text.Json.JsonSerializer.Deserialize<DataElement>(
                content,
                _options
            );
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

    /// <summary>
    /// Converts the instanceId (id) of the instance from {instanceGuid} to {instanceOwnerPartyId}/{instanceGuid} to be used outside cosmos.
    /// </summary>
    /// <param name="instance">the instance to preprocess</param>
    private static void PostProcess(Instance instance)
    {
        instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";
        if (instance.Data != null && instance.Data.Any())
        {
            SetReadStatus(instance);
        }

        (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
        instance.LastChanged = lastChanged;
        instance.LastChangedBy = lastChangedBy;
    }

    private static void SetReadStatus(Instance instance)
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
}
