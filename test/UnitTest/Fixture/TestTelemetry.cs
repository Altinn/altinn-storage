using System.Linq;
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

    public long? RequestsWithInvalidScopesCount()
    {
        Assert.True(_meterProvider.ForceFlush());

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
