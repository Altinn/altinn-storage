#nullable disable

using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Add repositories to DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds repositories to DI container.
    /// </summary>
    /// <remarks>
    /// The data source reads <see cref="PostgreSqlSettings"/> when it is first resolved, so it uses
    /// the final configuration, including sources added after service registration.
    /// </remarks>
    /// <param name="services">service collection.</param>
    /// <returns></returns>
    public static IServiceCollection AddRepositoriesPostgreSQL(this IServiceCollection services)
    {
        return services
            .AddSingleton<IApplicationRepository, PgApplicationRepository>()
            .AddSingleton<ITextRepository, PgTextRepository>()
            .AddSingleton<IDataRepository, PgDataRepository>()
            .AddSingleton<IInstanceEventRepository, PgInstanceEventRepository>()
            .AddSingleton<IInstanceRepository, PgInstanceRepository>()
            .AddSingleton<OutboxInsertRowFactory>()
            .AddSingleton<IInstanceMutationRepository, PgInstanceMutationRepository>()
            .AddSingleton<IBlobRepository, BlobRepository>()
            .AddSingleton<IA2Repository, PgA2Repository>()
            .AddSingleton<IOutboxRepository, PgOutboxRepository>()
            .AddSingleton<IInstanceLockRepository, PgInstanceLockRepository>()
            .AddSingleton<IMetricsRepository, PgMetricsRepository>()
            .AddSingleton<IStorageAccountRepository, PgStorageAccountRepository>()
            .AddNpgsqlDataSource(string.Empty, (serviceProvider, builder) =>
            {
                PostgreSqlSettings settings = serviceProvider
                    .GetRequiredService<IOptions<PostgreSqlSettings>>()
                    .Value;
                builder.ConnectionStringBuilder.ConnectionString = string.Format(
                    settings.ConnectionString,
                    settings.StorageDbPwd
                );
                builder
                    .EnableParameterLogging(settings.LogParameters)
                    .ConfigureJsonOptions(PersistedJson.Options)
                    .EnableDynamicJson()
                    .ConfigureTracing(o =>
                        o.ConfigureCommandSpanNameProvider(cmd => cmd.CommandText)
                            .ConfigureCommandFilter(cmd => true)
                            .EnableFirstResponseEvent(false)
                    );
            });
    }
}
