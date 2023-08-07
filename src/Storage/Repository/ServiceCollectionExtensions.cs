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
        /// <returns></returns>
        public static IServiceCollection AddRepositoriesCosmos(this IServiceCollection services)
        {
            return services
                .AddRepository<IDataRepository, DataRepository>()
                .AddRepository<IInstanceEventRepository, InstanceEventRepository>()
                .AddRepository<IInstanceRepository, InstanceRepository>()
                .AddRepository<IApplicationRepository, ApplicationRepository>()
                .AddRepository<ITextRepository, TextRepository>();
        }

        /// <summary>
        /// Adds test repositories to DI container.
        /// </summary>
        /// <param name="services">service collection.</param>
        /// <param name="connectionString">PostgreSQL connection string.</param>
        /// <param name="logParameters">Whether to log parameters.</param>
        /// <returns></returns>
        public static IServiceCollection AddTestRepositories(this IServiceCollection services, string connectionString, bool logParameters)
        {
            return services
                .AddSingleton<ITestInstanceRepository, TestInstanceRepository>()
                .AddNpgsqlDataSource(connectionString, builder => builder.EnableParameterLogging(logParameters));
        }

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
                .AddNpgsqlDataSource(connectionString, builder => builder.EnableParameterLogging(logParameters));
        }

        private static IServiceCollection AddRepository<TIRepo, TRepo>(this IServiceCollection services)
            where TIRepo : class
            where TRepo : class, IHostedService, TIRepo
        {
            return services
                .AddSingleton<TIRepo, TRepo>()
                .AddHostedService(sp => (TRepo)sp.GetRequiredService<TIRepo>());
        }
    }
}
