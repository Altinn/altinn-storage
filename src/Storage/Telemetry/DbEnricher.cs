using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Npgsql;

namespace Altinn.Platform.Storage.Telemetry
{
    /// <summary>
    /// Provides methods to enrich telemetry data with database command information.
    /// </summary>
    public static class DbEnricher
    {
        /// <summary>
        /// Enriches the specified activity with the parameters of the given NpgsqlCommand.
        /// </summary>
        /// <param name="activity">The activity to enrich.</param>
        /// <param name="cmd">The NpgsqlCommand containing the parameters to add to the activity.</param>
        public static void Enrich(Activity activity, NpgsqlCommand cmd)
        {
            var parameterWrapper = cmd.Parameters.Select((p, i) => new { Id=i, p.ParameterName, p.Value });
            activity.SetTag("Parameters", JsonSerializer.Serialize(parameterWrapper));
        }
    }
}
