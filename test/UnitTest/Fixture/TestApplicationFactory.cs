#nullable disable

using System.Diagnostics.Metrics;
using System.Threading;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

public sealed class TestApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ServiceUtil.EnsureDatabaseMigrated();

        IConfiguration configuration = new ConfigurationBuilder().AddTestSettings().Build();
        builder.ConfigureAppConfiguration(
            (hostingContext, config) =>
            {
                config.AddConfiguration(configuration);
            }
        );
        builder.ConfigureTestServices(
            (services) =>
            {
                // A handler with a finite lifetime is tracked by a process-wide timer that keeps the
                // host's service provider reachable after the host is disposed, so every host a test
                // run creates would stay in memory for the handler lifetime.
                services.ConfigureHttpClientDefaults(httpClient =>
                    httpClient.SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                );

                services.AddSingleton<RequestTracker>();

                ConcurrentList<MetricSnapshot> metricsList = [];
                services.AddSingleton(sp =>
                    ActivatorUtilities.CreateInstance<TestTelemetry>(sp, metricsList)
                );
                services
                    .AddOpenTelemetry()
                    .WithMetrics(metrics =>
                    {
                        metrics.AddInMemoryExporter(metricsList);

                        // A meter listener gets the meters of all test hosts in the process.
                        // This view drops the meters that the IMeterFactory of a different host makes.
                        ((IDeferredMeterProviderBuilder)metrics).Configure(
                            (sp, meterProviderBuilder) =>
                            {
                                IMeterFactory meterFactory = sp.GetRequiredService<IMeterFactory>();
                                meterProviderBuilder.AddView(instrument =>
                                    instrument.Meter.Scope is null
                                    || ReferenceEquals(instrument.Meter.Scope, meterFactory)
                                        ? null
                                        : MetricStreamConfiguration.Drop
                                );
                            }
                        );
                    });
            }
        );
    }
}
