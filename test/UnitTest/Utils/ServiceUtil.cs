#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class ServiceUtil
{
    private static readonly object _lock = new();
    private static ServiceProvider _serviceProvider;

    public static List<object> GetServices(
        List<Type> interfaceTypes,
        Dictionary<string, string> envVariables = null
    )
    {
        ServiceProvider serviceProvider = GetServiceProvider(envVariables);

        List<object> outputServices = new();
        foreach (Type interfaceType in interfaceTypes)
        {
            var outputServiceObject = serviceProvider.GetServices(interfaceType)!;
            outputServices.AddRange(outputServiceObject!);
        }

        return outputServices;
    }

    /// <summary>
    /// The single <see cref="NpgsqlDataSource"/> shared by all tests. Register this instead of
    /// creating a new one so the whole run shares one connection pool.
    /// </summary>
    public static NpgsqlDataSource GetSharedDataSource()
    {
        return GetServiceProvider(null).GetRequiredService<NpgsqlDataSource>();
    }

    public static string GetAppsettingsPath()
    {
        return "appsettings.unittest.json";
    }

    private static ServiceProvider GetServiceProvider(Dictionary<string, string> envVariables)
    {
        if (_serviceProvider != null)
        {
            return _serviceProvider;
        }

        lock (_lock)
        {
            if (_serviceProvider != null)
            {
                return _serviceProvider;
            }

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

            WebApplication.CreateBuilder().Build().SetUpPostgreSql(true, config);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddPostgresRepositories(config);
            services.AddMemoryCache();

            services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));

            _serviceProvider = services.BuildServiceProvider();
        }

        return _serviceProvider;
    }
}
