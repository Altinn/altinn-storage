#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class ServiceUtil
{
    private static readonly Lazy<ServiceProvider> _serviceProvider = new(BuildServiceProvider);

    public static List<object> GetServices(List<Type> interfaceTypes)
    {
        ServiceProvider serviceProvider = _serviceProvider.Value;

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
        return _serviceProvider.Value.GetRequiredService<NpgsqlDataSource>();
    }

    public static string GetAppsettingsPath()
    {
        return "appsettings.unittest.json";
    }

    /// <summary>
    /// Adds appsettings.unittest.json, pointed at the PostgreSQL and Azurite test containers.
    /// </summary>
    public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder)
    {
        return builder
            .AddJsonFile(GetAppsettingsPath())
            .AddInMemoryCollection(TestContainers.Settings);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var config = new ConfigurationBuilder().AddTestSettings().AddEnvironmentVariables().Build();

        WebApplication.CreateBuilder().Build().SetUpPostgreSql(true, config);

        IServiceCollection services = new ServiceCollection();

        services.AddLogging();
        services.AddPostgresRepositories(config);
        services.AddMemoryCache();

        services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));
        services.Configure<AzureStorageConfiguration>(
            config.GetSection("AzureStorageConfiguration")
        );
        services.AddSingleton<IStorageAccountResolver, StorageAccountResolver>();

        return services.BuildServiceProvider();
    }
}
