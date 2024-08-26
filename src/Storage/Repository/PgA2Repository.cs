using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Handles a2 repository.
    /// </summary>
    public class PgA2Repository : IA2Repository
    {
        private static readonly string _readXslSql = "select * from storage.reada2xsls (@_org, @_app, @_lformid, @_language, @_xsltype)";
        private static readonly string _insertXslSql = "call storage.inserta2xsl (@_org, @_app, @_lformid, @_language, @_pagenumber, @_xsl, @_xsltype)";
        private static readonly string _insertCodelistSql = "call storage.inserta2codelist (@_name, @_language, @_version, @_codelist)";
        private static readonly string _insertImageSql = "call storage.inserta2image (@_name, @_image)";
        private static readonly string _readCodelistSql = "select * from storage.reada2codelist (@_name, @_language)";
        private static readonly string _readImageSql = "select * from storage.reada2image (@_name)";

        private static readonly string _readMigrationStateSql = "select * from storage.reada2migrationstate (@_a2archivereference)";
        private static readonly string _insertMigrationStateSql = "call storage.inserta2migrationstate (@_a2archivereference)";
        private static readonly string _updateMigrationStateStartedSql = "call storage.updatea2migrationstatestarted (@_a2archivereference, @_instanceguid)";
        private static readonly string _updateMigrationStateCompletedSql = "call storage.updatea2migrationstatecompleted (@_instanceguid)";
        private static readonly string _deleteMigrationStateSql = "call storage.deletea2migrationstate (@_instanceguid)";

        private readonly NpgsqlDataSource _dataSource;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<PgA2Repository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgA2Repository"/> class.
        /// </summary>
        /// <param name="generalSettings">the general settings</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="logger">Logger</param>
        /// <param name="telemetryClient">Telemetry client</param>
        public PgA2Repository(
            IOptions<GeneralSettings> generalSettings,
            NpgsqlDataSource dataSource,
            ILogger<PgA2Repository> logger,
            TelemetryClient telemetryClient = null)
        {
            _dataSource = dataSource;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task CreateXsl(string org, string app, int lformId, string language, int pageNumber, string xsl, int xslType)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertXslSql);
            pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue("_app", NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue("_lformid", NpgsqlDbType.Integer, lformId);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
            pgcom.Parameters.AddWithValue("_pagenumber", NpgsqlDbType.Integer, pageNumber);
            pgcom.Parameters.AddWithValue("_xsl", NpgsqlDbType.Text, xsl);
            pgcom.Parameters.AddWithValue("_xsltype", NpgsqlDbType.Integer, xslType);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task CreateCodelist(string name, string language, int version, string codelist)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertCodelistSql);
            pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
            pgcom.Parameters.AddWithValue("_version", NpgsqlDbType.Integer, version);
            pgcom.Parameters.AddWithValue("_codelist", NpgsqlDbType.Text, codelist);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task CreateImage(string name, byte[] image)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertImageSql);
            pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);
            pgcom.Parameters.AddWithValue("_image", NpgsqlDbType.Bytea, image);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task<List<string>> GetXsls(string org, string app, int lformId, string language, int xslType)
        {
            List<string> xsls = [];

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readXslSql);
            pgcom.Parameters.AddWithValue("_org",      NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue("_app",      NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue("_lformid",   NpgsqlDbType.Integer, lformId);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
            pgcom.Parameters.AddWithValue("_xsltype", NpgsqlDbType.Integer, xslType);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                xsls.Add(reader.GetFieldValue<string>("xsl"));
            }

            tracker.Track();
            return xsls;
        }

        /// <inheritdoc/>
        public async Task<byte[]> GetImage(string name)
        {
            byte[] image = null;

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readImageSql);
            pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                image = reader.GetFieldValue<byte[]>("image");
            }

            tracker.Track();
            return image;
        }

        /// <inheritdoc/>
        public async Task<string> GetCodelist(string name, string preferredLanguage)
        {
            string codelist = null;
            string language = preferredLanguage;

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readCodelistSql);
            pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);

            while (codelist == null && (language == preferredLanguage || language != "nb"))
            {
                pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
                language = "nb";
                using TelemetryTracker tracker = new(_telemetryClient, pgcom);
                await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    codelist = reader.GetFieldValue<string>("codelist");
                }

                tracker.Track();
            }

            return codelist ?? string.Empty;
        }

        /// <inheritdoc/>
        public async Task CreateMigrationState(int a2ArchiveReference)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertMigrationStateSql);
            pgcom.Parameters.AddWithValue("_a2archivereference", NpgsqlDbType.Bigint, a2ArchiveReference);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task UpdateStartMigrationState(int a2ArchiveReference, string instanceGuid)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateMigrationStateStartedSql);
            pgcom.Parameters.AddWithValue("_a2archivereference", NpgsqlDbType.Bigint, a2ArchiveReference);
            pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task UpdateCompleteMigrationState(string instanceGuid)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateMigrationStateCompletedSql);
            pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task DeleteMigrationState(string instanceGuid)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteMigrationStateSql);
            pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
        }

        /// <inheritdoc/>
        public async Task<string> GetMigrationInstanceId(int a2ArchiveReference)
        {
            string instanceId = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readMigrationStateSql);
            pgcom.Parameters.AddWithValue("_a2archivereference", NpgsqlDbType.Bigint, a2ArchiveReference);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                instanceId = reader.IsDBNull("instanceguid") ? null : reader.GetFieldValue<Guid>("instanceguid").ToString();
            }

            tracker.Track();
            return instanceId;
        }
    }
}
