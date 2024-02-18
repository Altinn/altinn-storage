using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class ServiceUtil
{
    public static List<object> GetServices(List<Type> interfaceTypes, Dictionary<string, string> envVariables = null)
    {
        if (envVariables != null)
        {
            foreach (var item in envVariables)
            {
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }

        var builder = new ConfigurationBuilder()
            .AddJsonFile(GetAppsettingsPath())
            .AddEnvironmentVariables();

        var config = builder.Build();

        WebApplication.CreateBuilder()
                       .Build()
                       .SetUpPostgreSql(true, config);

        IServiceCollection services = new ServiceCollection();

        services.AddLogging();
        services.AddPostgresRepositories(config);
        services.AddMemoryCache();

        services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));

        var serviceProvider = services.BuildServiceProvider();
        List<object> outputServices = new();

        foreach (Type interfaceType in interfaceTypes)
        {
            var outputServiceObject = serviceProvider.GetServices(interfaceType)!;
            outputServices.AddRange(outputServiceObject!);
        }

        return outputServices;
    }

    public static string GetAppsettingsPath()
    {
        ////return @$"{System.IO.Directory.GetCurrentDirectory()}\..\..\..\appsettings.json";
        return "appsettings.json";
    }
}
