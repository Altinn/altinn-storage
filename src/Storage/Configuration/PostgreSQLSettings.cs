namespace Altinn.Platform.Storage.Configuration
{
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
    }
}
