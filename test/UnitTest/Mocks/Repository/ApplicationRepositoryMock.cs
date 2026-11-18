#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class ApplicationRepositoryMock : IApplicationRepository
{
    public Task<Application> Create(Application item)
    {
        throw new NotImplementedException();
    }

    public Task<bool> Delete(string appId, string org)
    {
        throw new NotImplementedException();
    }

    public async Task<Application> FindOne(
        string appId,
        string org,
        CancellationToken cancellationToken = default
    )
    {
        return await Task.FromResult(GetTestApplication(org, appId.Split("/")[1]));
    }

    public Task<List<Application>> FindAll()
    {
        throw new NotImplementedException();
    }

    public Task<List<Application>> FindByOrg(string org)
    {
        throw new NotImplementedException();
    }

    public Task<Application> Update(Application item)
    {
        throw new NotImplementedException();
    }

    private static Application GetTestApplication(string org, string app)
    {
        string applicationPath = Path.Combine(
            GetAppsPath(),
            org,
            app,
            "config",
            "applicationmetadata.json"
        );
        if (File.Exists(applicationPath))
        {
            string content = File.ReadAllText(applicationPath);
            Application application = JsonConvert.DeserializeObject<Application>(content);
            return application;
        }

        return null;
    }

    private static string GetAppsPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(ApplicationRepositoryMock).Assembly.Location).LocalPath
        );
        return Path.Combine(unitTestFolder, "..", "..", "..", "data", "apps");
    }
}
