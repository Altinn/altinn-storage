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
        /// Adds repositories to DI container.
        /// </summary>
        /// <param name="services">service collection.</param>
        /// <returns></returns>
        public static IServiceCollection AddRepositoriesPostgreSQL(this IServiceCollection services)
        {
            return services
                .AddRepository<IApplicationRepository, ApplicationRepository>()
                .AddRepository<ITextRepository, TextRepository>()
                .AddRepository<IDataRepository, PgDataRepository>()
                .AddRepository<IInstanceEventRepository, PgInstanceEventRepository>()
                .AddRepository<IInstanceRepository, PgInstanceRepository>();
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
