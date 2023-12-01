using System;
using System.IO;
using Altinn.Platform.Storage.UnitTest.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Yuniql.AspNetCore;
using Yuniql.PostgreSql;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

/// <summary>
/// Extension class for web application
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Configure and set up db
    /// </summary>
    /// <param name="app">app</param>
    /// <param name="isDevelopment">is environment dev</param>
    /// <param name="config">the configuration collection</param>
    public static void SetUpPostgreSql(this IApplicationBuilder app, bool isDevelopment, IConfiguration config)
    {
        PostgreSqlSettings? settings = config.GetSection("PostgreSQLSettings")
            .Get<PostgreSqlSettings>()
            ?? throw new ArgumentNullException(nameof(config), "Required PostgreSQLSettings is missing from application configuration");

        if (settings.EnableDBConnection)
        {
            ConsoleTraceService traceService = new() { IsDebugEnabled = true };

            string connectionString = string.Format(settings.AdminConnectionString, settings.StorageDbAdminPwd);

            string fullWorkspacePath = isDevelopment ?
                Path.Combine(Directory.GetParent(Environment.CurrentDirectory)!.FullName, settings.MigrationScriptPath) :
                Path.Combine(Environment.CurrentDirectory, settings.MigrationScriptPath);

            app.UseYuniql(
                new PostgreSqlDataService(traceService),
                new PostgreSqlBulkImportService(traceService),
                traceService,
                new Yuniql.AspNetCore.Configuration
                {
                    Workspace = fullWorkspacePath,
                    ConnectionString = connectionString,
                    IsAutoCreateDatabase = false,
                    IsDebug = settings.EnableDebug
                });
        }
    }
}
