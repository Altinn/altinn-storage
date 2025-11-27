using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

public sealed class TestApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(
            (services) =>
            {
                ConcurrentList<MetricSnapshot> metricsList = [];
                services.AddSingleton(sp =>
                    ActivatorUtilities.CreateInstance<TestTelemetry>(sp, metricsList)
                );
                services
                    .AddOpenTelemetry()
                    .WithMetrics(metrics =>
                    {
                        metrics.AddInMemoryExporter(metricsList);
                    });
            }
        );
    }
}
