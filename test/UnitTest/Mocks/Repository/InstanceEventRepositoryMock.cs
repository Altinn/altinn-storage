#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class InstanceEventRepositoryMock : IInstanceEventRepository
{
    public Task<int> DeleteAllInstanceEvents(Guid instanceGuid)
    {
        throw new NotImplementedException();
    }

    public Task<InstanceEvent> GetOneEvent(Guid instanceGuid, Guid eventGuid)
    {
        throw new NotImplementedException();
    }

    public Task<InstanceEvent> InsertInstanceEvent(
        InstanceEvent instanceEvent,
        InstanceInternal instance = null
    )
    {
        return Task.FromResult(instanceEvent);
    }

    public async Task<List<InstanceEvent>> ListInstanceEvents(
        Guid instanceGuid,
        string[] eventTypes,
        DateTime? fromDateTime,
        DateTime? toDateTime
    )
    {
        List<InstanceEvent> events = new List<InstanceEvent>();

        lock (TestDataUtil.DataLock)
        {
            string eventsPath = GetInstanceEventsPath();
            if (Directory.Exists(eventsPath))
            {
                string[] instanceEventPath = Directory.GetFiles(eventsPath);
                foreach (string path in instanceEventPath)
                {
                    string content = File.ReadAllText(path);
                    InstanceEvent instance = JsonConvert.DeserializeObject<InstanceEvent>(content);
                    events.Add(instance);
                }
            }
        }

        return await Task.FromResult(events);
    }

    private static string GetInstanceEventsPath()
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
            "instanceEvents"
        );
    }
}
