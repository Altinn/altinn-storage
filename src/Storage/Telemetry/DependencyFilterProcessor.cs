using System;
using System.Diagnostics;
using Altinn.Platform.Storage.Configuration;
using OpenTelemetry;

namespace Altinn.Platform.Storage.Telemetry
{
    /// <summary>
    /// Filter to avoid dependency telemetry for migrations
    /// </summary>
    public class DependencyFilterProcessor : BaseProcessor<Activity>
    {
        private readonly bool _disableTelemetryForMigration;

        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyFilterProcessor"/> class.
        /// </summary>
        public DependencyFilterProcessor(GeneralSettings generalSettings) : base()
        {
            _disableTelemetryForMigration = generalSettings.DisableTelemetryForMigration;
        }

        /// <summary>
        /// Filter to avoid dependency telemetry for migrations
        /// </summary>
        public override void OnEnd(Activity activity)
        {
            if (_disableTelemetryForMigration && !OKtoSend(activity))
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
