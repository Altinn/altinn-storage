using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Altinn.Platform.Telemetry
{
    /// <summary>
    /// Set up custom telemetry for Application Insights
    /// </summary>
    public class CustomTelemetryInitializer : ITelemetryInitializer
    {
        /// <summary>
        /// Custom TelemetryInitializer that sets some specific values for the component
        /// </summary>
        public void Initialize(ITelemetry telemetry)
        {
            if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
            {
                telemetry.Context.Cloud.RoleName = "platform-storage";
            }

            // Disable sampling for exceptions, requests, dependencies and cleanup
            if (telemetry is RequestTelemetry requestTelemetry)
            {
                ((ISupportSampling)telemetry).SamplingPercentage = 100;
                requestTelemetry.ProactiveSamplingDecision = SamplingDecision.SampledIn;
            }
            else if (telemetry is DependencyTelemetry dependencyTelemetry)
            {
                ((ISupportSampling)telemetry).SamplingPercentage = 100;
                dependencyTelemetry.ProactiveSamplingDecision = SamplingDecision.SampledIn;
            }
            else if (telemetry is ExceptionTelemetry exceptionTelemetry)
            {
                ((ISupportSampling)telemetry).SamplingPercentage = 100;
                exceptionTelemetry.ProactiveSamplingDecision = SamplingDecision.SampledIn;
            }
            else if (telemetry is TraceTelemetry traceTelemetry
                && traceTelemetry.Properties.TryGetValue("RequestPath", out string path)
                && path.StartsWith("/storage/api/v1/cleanup", StringComparison.OrdinalIgnoreCase))
            {
                ((ISupportSampling)traceTelemetry).SamplingPercentage = 100;
                traceTelemetry.ProactiveSamplingDecision = SamplingDecision.SampledIn;
            }
        }
    }
}
