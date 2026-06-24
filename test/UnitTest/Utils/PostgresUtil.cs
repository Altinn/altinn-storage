#nullable disable

using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.UnitTest.Configuration;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class PostgresUtil
{
    private static readonly Lazy<NpgsqlDataSource> DataSource = new(CreateDataSource);

    public static async Task<int> RunCountQuery(string query)
    {
        await using NpgsqlCommand pgcom = DataSource.Value.CreateCommand(query);
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<int>(0);
            }
        }

        throw new Exception("No results for " + query);
    }

    public static async Task<T> RunQuery<T>(string query)
    {
        await using NpgsqlCommand pgcom = DataSource.Value.CreateCommand(query);
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<T>(0);
            }
        }

        throw new Exception("No results for " + query);
    }

    public static async Task<int> RunSql(string query)
    {
        await using NpgsqlCommand pgcom = DataSource.Value.CreateCommand(query);
        return await pgcom.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Overrides now() and clock_timestamp() to return the specified value.
    /// </summary>
    /// <param name="frozenAt">The timestamp to freeze time at</param>
    public static async Task FreezeTime(DateTimeOffset frozenAt)
    {
        await using NpgsqlCommand pgcom = DataSource.Value.CreateCommand(
            "SELECT test_override.freeze_time(@frozen_at)"
        );
        pgcom.Parameters.AddWithValue("frozen_at", frozenAt);
        await pgcom.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Unfreezes the database time, making now() and clock_timestamp() return the real current time.
    /// </summary>
    public static async Task UnfreezeTime()
    {
        await using NpgsqlCommand pgcom = DataSource.Value.CreateCommand(
            "SELECT test_override.unfreeze_time()"
        );
        await pgcom.ExecuteNonQueryAsync();
    }

    private static NpgsqlDataSource CreateDataSource()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(ServiceUtil.GetAppsettingsPath())
            .AddEnvironmentVariables()
            .Build();

        using WebApplication app = WebApplication.CreateBuilder().Build();
        app.SetUpPostgreSql(true, config);

        PostgreSqlSettings settings =
            config.GetSection("PostgreSQLSettings").Get<PostgreSqlSettings>()
            ?? throw new ArgumentNullException(
                nameof(config),
                "Required PostgreSQLSettings is missing from application configuration"
            );

        string connectionString = string.Format(settings.ConnectionString, settings.StorageDbPwd);
        return new NpgsqlDataSourceBuilder(connectionString).EnableDynamicJson().Build();
    }
}
