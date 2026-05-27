using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Implementation of <see cref="IMetricsRepository"/>
/// </summary>
public class PgMetricsRepository(NpgsqlDataSource dataSource) : IMetricsRepository
{
    private const string _getDailyInstanceMetric =
        "SELECT * FROM storage.get_instance_metrics($1, $2, $3)";

    /// <inheritdoc/>
    public async Task<DailyMetrics<DailyInstanceMetricsRecord>> GetDailyInstanceMetrics(
        int day,
        int month,
        int year,
        CancellationToken cancellationToken
    )
    {
        DailyMetrics<DailyInstanceMetricsRecord> metrics = new()
        {
            Day = day,
            Month = month,
            Year = year,
        };

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_getDailyInstanceMetric);
        pgcom.CommandTimeout = 900; // 15 minutes

        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, day);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, month);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, year);
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                string appid = await reader.GetFieldValueAsync<string>("appid", cancellationToken);
                DailyInstanceMetricsRecord instanceRow = new()
                {
                    ServiceOwnerCode = appid.Split('/')[0],
                    ResourceTitle = appid.Split('/')[1],
                    InstanceCount = await reader.GetFieldValueAsync<long>(
                        "completed_instances",
                        cancellationToken
                    ),
                };

                metrics.Metrics.Add(instanceRow);
            }
        }

        return metrics;
    }
}
