using System;
using System.Diagnostics;
using OpenTelemetry;

namespace Altinn.Platform.Storage.Telemetry
{
    /// <summary>
    /// Filter to avoid dependency telemetry for migrations
    /// </summary>
    public class DependencyFilterProcessor : BaseProcessor<Activity>
    {
        /// <summary>
        /// Filter to avoid dependency telemetry for migrations
        /// </summary>
        public override void OnEnd(Activity activity)
        {
            if (!OKtoSend(activity))
            {
                activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            }
        }

        private bool OKtoSend(Activity activity)
        {
            return activity is not null && !activity.OperationName.StartsWith("POST Migration", StringComparison.OrdinalIgnoreCase);
        }
    }
}
