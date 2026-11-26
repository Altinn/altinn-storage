#nullable disable

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Settings for Postgres database
/// </summary>
public class PostgreSqlSettings
{
    /// <summary>
    /// Connection string for the postgres db
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// Password for app user for the postgres db
    /// </summary>
    public string StorageDbPwd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include parameter values in logging/tracing.
    /// </summary>
    public bool LogParameters { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the application applies the database migrations
    /// when it starts. The tests turn it off, as they migrate the database once for the whole run.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;
}
