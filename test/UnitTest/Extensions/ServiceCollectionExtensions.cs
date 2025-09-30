using System;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

/// <summary>
/// Extension class for <see cref="IServiceCollection"/>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds persistence services to DI container.
    /// </summary>
    /// <param name="services">service collection.</param>
    /// <param name="config">the configuration collection</param>
    public static IServiceCollection AddPostgresRepositories(this IServiceCollection services, IConfiguration config)
    {
        PostgreSqlSettings settings = config.GetSection("PostgreSQLSettings")
            .Get<PostgreSqlSettings>()
            ?? throw new ArgumentNullException(nameof(config), "Required PostgreSQLSettings is missing from application configuration");

        string connectionString = string.Format(settings.ConnectionString, settings.StorageDbPwd);

        return services
            .AddSingleton<IApplicationRepository, PgApplicationRepository>()
            .AddSingleton<ITextRepository, PgTextRepository>()
            .AddSingleton<IDataRepository, PgDataRepository>()
            .AddSingleton<IInstanceEventRepository, PgInstanceEventRepository>()
            .AddSingleton<IInstanceRepository, PgInstanceRepository>()
            .AddSingleton<IInstanceAndEventsRepository, PgInstanceAndEventsRepository>()
            .AddSingleton<IBlobRepository, BlobRepository>()
            .AddSingleton<IOutboxRepository, PgOutboxRepository>()
            .AddNpgsqlDataSource(connectionString, builder => builder.EnableDynamicJson());
    }
}
