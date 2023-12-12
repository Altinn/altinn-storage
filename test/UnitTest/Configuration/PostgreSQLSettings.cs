namespace Altinn.Platform.Storage.UnitTest.Configuration;

/// <summary>
/// Settings for Postgre database
/// </summary>
public class PostgreSqlSettings
{
    /// <summary>
    /// Boolean indicating if database should be connected
    /// </summary>
    public bool EnableDBConnection { get; set; } = true;

    /// <summary>
    /// Path to migration scripts
    /// </summary>
    public string MigrationScriptPath { get; set; } = string.Empty;

    /// <summary>
    /// Connection string for the admin user of postgre db
    /// </summary>
    public string AdminConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Password for admin user for the postgre db
    /// </summary>
    public string StorageDbAdminPwd { get; set; } = string.Empty;

    /// <summary>
    /// Connection string for app user the postgre db
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Password for app user for the postgre db
    /// </summary>
    public string StorageDbPwd { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to include parameter values in logging/tracing
    /// </summary>
    public bool LogParameters { get; set; } = false;

    /// <summary>
    /// Boolean indicating if connection to db should be in debug mode
    /// </summary>
    public bool EnableDebug { get; set; } = false;
}
