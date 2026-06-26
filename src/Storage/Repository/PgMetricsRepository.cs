using System;
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
        DateTime dateTime,
        CancellationToken cancellationToken
    )
    {
        DailyMetrics<DailyInstanceMetricsRecord> metrics = new() { DateTime = dateTime };

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_getDailyInstanceMetric);
        pgcom.CommandTimeout = 900; // 15 minutes

        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, dateTime.Day);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, dateTime.Month);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, dateTime.Year);
        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DailyInstanceMetricsRecord instanceRow = await GenerateInstanceMetricsRecord(
                reader,
                cancellationToken
            );
            metrics.Metrics.Add(instanceRow);
        }

        return metrics;
    }

    private static async Task<DailyInstanceMetricsRecord> GenerateInstanceMetricsRecord(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken
    )
    {
        string appId = await reader.GetFieldValueAsync<string>("appid", cancellationToken);
        if (!ValidateAppId(appId, out string[] appIdParts))
        {
            throw new DataException(
                $"Unexpected appid format returned from sql function storage.get_instance_metrics: '{appId}'."
            );
        }
        string org = appIdParts[0];
        string app = appIdParts[1];

        return new DailyInstanceMetricsRecord
        {
            ServiceOwnerCode = org,
            ResourceTitle = app,
            ResourceId = GetAppResourceId(org, app),
            InstanceCount = await reader.GetFieldValueAsync<long>(
                "completed_instances",
                cancellationToken
            ),
        };
    }

    private static string GetAppResourceId(string org, string app) => $"app_{org}_{app}";

    private static bool ValidateAppId(string appId, out string[] appIdParts)
    {
        appIdParts = appId.Split('/');
        return appIdParts.Length == 2
            && !string.IsNullOrWhiteSpace(appIdParts[0])
            && !string.IsNullOrWhiteSpace(appIdParts[1]);
    }
}
