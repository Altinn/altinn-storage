using System.Collections.Generic;
using System.Linq;
using OpenTelemetry.Metrics;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

internal class TestTelemetry
{
    public List<MetricSnapshot> Metrics { get; } = [];

    public long? GetCounterValue(string metricName)
    {
        var metric = Metrics.LastOrDefault(m => m.Name == metricName);
        if (metric == null)
        {
            return null;
        }

        Assert.Equal(MetricType.LongSum, metric.MetricType);
        if (metric.MetricPoints.Count == 0)
        {
            return null;
        }

        var point = metric.MetricPoints[^1];
        return point.GetSumLong();
    }
}
