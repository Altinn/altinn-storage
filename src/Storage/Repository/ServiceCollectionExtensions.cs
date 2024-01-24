namespace Altinn.Platform.Storage.Repository
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

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
        public static IServiceCollection AddRepositoriesPostgreSQL(this IServiceCollection services, string connectionString, bool logParameters)
        {
            return services
                .AddSingleton<IApplicationRepository, PgApplicationRepository>()
                .AddSingleton<ITextRepository, PgTextRepository>()
                .AddSingleton<IDataRepository, PgDataRepository>()
                .AddSingleton<IInstanceEventRepository, PgInstanceEventRepository>()
                .AddSingleton<IInstanceRepository, PgInstanceRepository>()
                .AddSingleton<IBlobRepository, BlobRepository>()
                .AddNpgsqlDataSource(connectionString, builder => builder.EnableParameterLogging(logParameters).EnableDynamicJson());
        }
    }
}
