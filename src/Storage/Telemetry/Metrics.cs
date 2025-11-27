using System.Diagnostics.Metrics;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Metrics
/// </summary>
internal static class Metrics
{
    /// <summary>
    /// Metrics for this application
    /// </summary>
    public static readonly Meter Meter = new("Altinn.Platform.Storage");
}
