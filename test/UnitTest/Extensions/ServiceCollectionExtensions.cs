#nullable disable

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
    public static IServiceCollection AddPostgresRepositories(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        PostgreSqlSettings settings =
            config.GetSection("PostgreSQLSettings").Get<PostgreSqlSettings>()
            ?? throw new ArgumentNullException(
                nameof(config),
                "Required PostgreSQLSettings is missing from application configuration"
            );

        string connectionString = string.Format(settings.ConnectionString, settings.StorageDbPwd);

        services.AddNpgsqlDataSource(connectionString, builder => builder.EnableDynamicJson());

        return services.AddRepositoryImplementations();
    }

    /// <summary>
    /// Registers the repository implementations, expecting an <see cref="NpgsqlDataSource"/> to be
    /// registered separately so callers can share one data source instead of opening another pool.
    /// </summary>
    /// <param name="services">service collection.</param>
    public static IServiceCollection AddRepositoryImplementations(this IServiceCollection services)
    {
        return services
            .AddSingleton<IApplicationRepository, PgApplicationRepository>()
            .AddSingleton<ITextRepository, PgTextRepository>()
            .AddSingleton<IDataRepository, PgDataRepository>()
            .AddSingleton<IInstanceEventRepository, PgInstanceEventRepository>()
            .AddSingleton<IInstanceRepository, PgInstanceRepository>()
            .AddSingleton<OutboxInsertRowFactory>()
            .AddSingleton<IInstanceMutationRepository, PgInstanceMutationRepository>()
            .AddSingleton<IInstanceAndEventsRepository, PgInstanceAndEventsRepository>()
            .AddSingleton<IBlobRepository, BlobRepository>()
            .AddSingleton<IOutboxRepository, PgOutboxRepository>()
            .AddSingleton<IInstanceLockRepository, PgInstanceLockRepository>();
    }
}
