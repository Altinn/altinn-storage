using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Implementation of <see cref="IStudioMetricsRepository"/>
/// </summary>
public class PgStudioMetricsRepository(NpgsqlDataSource dataSource) : IStudioMetricsRepository
{
    private const string _getDailyMetrics =
        "SELECT * FROM storage.get_studio_daily_metrics($1, $2)";

    private const int _commandTimeoutSeconds = 900;

    /// <inheritdoc/>
    public async Task<StudioDailyMetrics> GetDailyMetrics(
        string org,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        StudioDailyMetrics metrics = new()
        {
            Org = org,
            Date = date,
            ComputedAt = DateTime.UtcNow,
        };

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_getDailyMetrics);
        pgcom.CommandTimeout = _commandTimeoutSeconds;
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Date, date);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string app = AppFromAppId(
                await reader.GetFieldValueAsync<string>("appid", cancellationToken)
            );
            metrics.Apps.Add(
                new StudioAppDailyMetrics
                {
                    App = app,
                    InstancesCreated = await reader.GetFieldValueAsync<long>(
                        "instances_created",
                        cancellationToken
                    ),
                    InstancesCompleted = await reader.GetFieldValueAsync<long>(
                        "instances_completed",
                        cancellationToken
                    ),
                    SecondsToCompleteP50 = await reader.IsDBNullAsync(
                        "seconds_to_complete_p50",
                        cancellationToken
                    )
                        ? null
                        : await reader.GetFieldValueAsync<int>(
                            "seconds_to_complete_p50",
                            cancellationToken
                        ),
                }
            );
        }

        return metrics;
    }

    private static string AppFromAppId(string appId)
    {
        string[] parts = appId.Split('/');
        if (
            parts.Length != 2
            || string.IsNullOrWhiteSpace(parts[0])
            || string.IsNullOrWhiteSpace(parts[1])
        )
        {
            throw new DataException($"Unexpected appid format in storage.instances: '{appId}'.");
        }

        return parts[1];
    }
}
