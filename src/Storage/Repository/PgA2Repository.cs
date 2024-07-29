using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
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
        private static readonly string _readXslSql = "select * from storage.reada2xsls (@_org, @_app, @_lformid, @_language)";
        private static readonly string _insertXslSql = "call storage.inserta2xsl (@_org, @_app, @_lformid, @_language, @_pagenumber, @_xsl)";
        private static readonly string _insertCodelistSql = "call storage.inserta2codelist (@_name, @_language, @_version, @_codelist)";
        private static readonly string _insertImageSql = "call storage.inserta2image (@_name, @_image)";
        private static readonly string _readCodelistSql = "select * from storage.reada2codelist (@_name, @_language)";
        private static readonly string _readImageSql = "select * from storage.reada2image (@_name)";

        private readonly IMemoryCache _memoryCache;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsTitles;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsMetadata;
        private readonly string _cacheKey = "allAppTitles";
        private readonly NpgsqlDataSource _dataSource;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<PgA2Repository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgA2Repository"/> class.
        /// </summary>
        /// <param name="generalSettings">the general settings</param>
        /// <param name="memoryCache">the memory cache</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="logger">Logger</param>
        /// <param name="telemetryClient">Telemetry client</param>
        public PgA2Repository(
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache,
            NpgsqlDataSource dataSource,
            ILogger<PgA2Repository> logger,
            TelemetryClient telemetryClient = null)
        {
            _dataSource = dataSource;
            _telemetryClient = telemetryClient;
            _memoryCache = memoryCache;
            _cacheEntryOptionsTitles = new MemoryCacheEntryOptions()
                .SetPriority(CacheItemPriority.High)
                .SetAbsoluteExpiration(new TimeSpan(0, 0, generalSettings.Value.AppTitleCacheLifeTimeInSeconds));
            _cacheEntryOptionsMetadata = new MemoryCacheEntryOptions()
              .SetPriority(CacheItemPriority.High)
              .SetAbsoluteExpiration(new TimeSpan(0, 0, generalSettings.Value.AppMetadataCacheLifeTimeInSeconds));
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task CreateXsl(string org, string app, int lformId, string language, int pageNumber, string xsl)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertXslSql);
            pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue("_app", NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue("_lformid", NpgsqlDbType.Integer, lformId);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
            pgcom.Parameters.AddWithValue("_pagenumber", NpgsqlDbType.Integer, pageNumber);
            pgcom.Parameters.AddWithValue("_xsl", NpgsqlDbType.Text, xsl);
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
        public async Task<List<string>> GetXsls(string org, string app, int lformId, string language)
        {
            List<string> xsls = [];

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readXslSql);
            pgcom.Parameters.AddWithValue("_org",      NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue("_app",      NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue("_lformid",   NpgsqlDbType.Integer, lformId);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
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
            try
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
                _logger.LogError($"Debug get image, lookup: {name}, returned size: {image?.Length ?? -1}");
                return image;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Image error: " + ex.ToString());
                throw;
            }
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
    }
}
