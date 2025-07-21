using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Telemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

internal sealed class TestTelemetry
{
    private readonly MeterProvider _meterProvider;

    public ConcurrentList<MetricSnapshot> Metrics { get; }

    public TestTelemetry(MeterProvider meterProvider, ConcurrentList<MetricSnapshot> metrics)
    {
        _meterProvider = meterProvider;
        Metrics = metrics;
    }

    public async Task AssertRequestsWithInvalidScopesCountAsync(long? expectedCount)
    {
        const int maxAttempts = 10; // 100ms total (10 * 10ms)
        const int delayMs = 10;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Assert.True(_meterProvider.ForceFlush());

            var actualCount = GetInvalidScopesCount();
            if (actualCount == expectedCount)
            {
                return; // Success
            }

            await Task.Delay(delayMs);
        }

        // Final attempt without delay for assertion failure
        Assert.True(_meterProvider.ForceFlush());
        var finalCount = GetInvalidScopesCount();
        Assert.Equal(expectedCount, finalCount);
    }

    private long? GetInvalidScopesCount()
    {
        var metric = Metrics.LastOrDefault(m => m.Name == AspNetCoreMetricsEnricher.MetricName);
        if (metric == null)
        {
            return null;
        }

        Assert.Equal(MetricType.Histogram, metric.MetricType);
        if (metric.MetricPoints.Count == 0)
        {
            return null;
        }

        var points = metric.MetricPoints.Where(p =>
        {
            foreach (var tag in p.Tags)
            {
                if (tag.Key == "invalid_scopes" && tag.Value is true)
                {
                    return true;
                }
            }

            return false;
        }).ToArray();
        if (points.Length == 0)
        {
            return null;
        }

        var point = points[^1];
        return point.GetHistogramCount();
    }
}
