using Altinn.Platform.Storage.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Add repositories to DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds repositories to DI container.
    /// </summary>
    /// <param name="services">service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="logParameters">Whether to log parameters.</param>
    /// <returns></returns>
    public static IServiceCollection AddRepositoriesPostgreSQL(
        this IServiceCollection services,
        string connectionString,
        bool logParameters
    )
    {
        return services
            .AddSingleton<IApplicationRepository, PgApplicationRepository>()
            .AddSingleton<ITextRepository, PgTextRepository>()
            .AddSingleton<IDataRepository, PgDataRepository>()
            .AddSingleton<IInstanceEventRepository, PgInstanceEventRepository>()
            .AddSingleton<IInstanceRepository, PgInstanceRepository>()
            .AddSingleton<IInstanceAndEventsRepository, PgInstanceAndEventsRepository>()
            .AddSingleton<IBlobRepository, BlobRepository>()
            .AddSingleton<IA2Repository, PgA2Repository>()
            .AddSingleton<IOutboxRepository, PgOutboxRepository>()
            .AddSingleton<IInstanceLockRepository, PgInstanceLockRepository>()
            .AddNpgsqlDataSource(
                connectionString,
                builder =>
                    builder
                        .EnableParameterLogging(logParameters)
                        .EnableDynamicJson()
                        .ConfigureTracing(o =>
                            o.ConfigureCommandSpanNameProvider(cmd => cmd.CommandText)
                                .ConfigureCommandFilter(cmd => true)
                                .EnableFirstResponseEvent(false)
                        )
            );
    }
}
