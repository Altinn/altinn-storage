using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Handles applicationMetadata repository.
    /// </summary>
    public class PgApplicationRepository : IApplicationRepository
    {
        private static readonly string _readSql = "select application from storage.applications";
        private static readonly string _readByOrgSql = "select application from storage.applications where org = $1";
        private static readonly string _readByIdSql = "select application from storage.applications where app = $1 and org = $2";
        private static readonly string _deleteSql = "delete from storage.applications where app = $1 and org = $2";
        private static readonly string _updateSql = "update storage.applications set application = $3 where app = $1 and org = $2";
        private static readonly string _createSql = "insert into storage.applications (app, org, application) values ($1, $2, jsonb_strip_nulls($3))" +
            " on conflict on constraint app_org do update set application = jsonb_strip_nulls($3)";

        private readonly IMemoryCache _memoryCache;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsTitles;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsMetadata;
        private readonly string _cacheKey = "allAppTitles";
        private readonly NpgsqlDataSource _dataSource;
        private readonly TelemetryClient _telemetryClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgApplicationRepository"/> class.
        /// </summary>
        /// <param name="generalSettings">the general settings</param>
        /// <param name="memoryCache">the memory cache</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="telemetryClient">Telemetry client</param>
        public PgApplicationRepository(
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache,
            NpgsqlDataSource dataSource,
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
        }

        /// <inheritdoc/>
        public async Task<List<Application>> FindAll()
        {
            List<Application> applications = [];

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                applications.Add(reader.GetFieldValue<Application>("application"));
            }

            tracker.Track();
            return applications;
        }

        /// <inheritdoc/>
        public async Task<List<Application>> FindByOrg(string org)
        {
            List<Application> applications = [];

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readByOrgSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                applications.Add(reader.GetFieldValue<Application>("application"));
            }

            tracker.Track();
            return applications;
        }

        /// <inheritdoc/>
        public async Task<Application> FindOne(string appId, string org)
        {
            string cacheKey = $"aid:{appId}";
            if (!_memoryCache.TryGetValue(cacheKey, out Application application))
            {
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readByIdSql);
                using TelemetryTracker tracker = new(_telemetryClient, pgcom);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, GetAppFromAppId(appId));
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
                await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    application = reader.GetFieldValue<Application>("application");
                    _memoryCache.Set(cacheKey, application, _cacheEntryOptionsMetadata);
                }
                else
                {
                    application = null;
                }

                tracker.Track();
            }

            return application;
        }

        /// <inheritdoc/>
        public async Task<Application> Create(Application item)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_createSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, GetAppFromAppId(item.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, item.Org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, item);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
            return item;
        }

        /// <inheritdoc/>
        public async Task<Application> Update(Application item)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, GetAppFromAppId(item.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, item.Org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, item);

            await pgcom.ExecuteNonQueryAsync();

            _memoryCache.Set(item.Id, item, _cacheEntryOptionsMetadata);
            tracker.Track();
            return item;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string appId, string org)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, GetAppFromAppId(appId));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);

            var rc = await pgcom.ExecuteNonQueryAsync();
            tracker.Track();
            return rc == 1;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, string>> GetAllAppTitles()
        {
            if (!_memoryCache.TryGetValue(_cacheKey, out Dictionary<string, string> appTitles))
            {
                appTitles = [];
                foreach (Application item in await FindAll())
                {
                    StringBuilder titles = new();
                    if (item.Title?.Values != null)
                    {
                        foreach (string title in item.Title.Values)
                        {
                            titles.Append(title + ";");
                        }
                    }

                    appTitles.Add(item.Id, titles.ToString().TrimEnd(';'));
                }

                _memoryCache.Set(_cacheKey, appTitles, _cacheEntryOptionsTitles);
            }

            return appTitles;
        }

        private static string GetAppFromAppId(string appId)
        {
            return appId.Split('/')[1];
        }
    }
}
