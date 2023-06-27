using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        private static readonly string _readByIdSql = "select application from storage.applications where alternateid = $1";
        private static readonly string _deleteSql = "delete from storage.applications where alternateid = $1";
        private static readonly string _updateSql = "update storage.applications set application = $2 where alternateid = $1";
        private static readonly string _createSql = "insert into storage.applications (alternateid, org, application) values ($1, $2, $3)";

        private readonly IMemoryCache _memoryCache;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsTitles;
        private readonly MemoryCacheEntryOptions _cacheEntryOptionsMetadata;
        private readonly string _cacheKey = "allAppTitles";
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgApplicationRepository"/> class.
        /// </summary>
        /// <param name="generalSettings">the general settings</param>
        /// <param name="memoryCache">the memory cache</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgApplicationRepository(
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache,
            NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
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
            List<Application> applications = new();

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                applications.Add(SetLegacyId(reader.GetFieldValue<Application>("application")));
            }

            return applications;
        }

        /// <inheritdoc/>
        public async Task<List<Application>> FindByOrg(string org)
        {
            List<Application> applications = new();

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readByOrgSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                applications.Add(SetLegacyId(reader.GetFieldValue<Application>("application")));
            }

            return applications;
        }

        /// <inheritdoc/>
        public async Task<Application> FindOne(string appId, string org)
        {
            if (!_memoryCache.TryGetValue(appId, out Application application))
            {
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readByIdSql);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, appId.Replace('/', '-'));
                await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    application = SetLegacyId(reader.GetFieldValue<Application>("application"));
                    _memoryCache.Set(appId, application, _cacheEntryOptionsMetadata);
                }
                else
                {
                    application = null;
                }
            }

            return application;
        }

        /// <inheritdoc/>
        public async Task<Application> Create(Application item)
        {
            SetInternalId(item);
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_createSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, item.Id);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, item.Org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, item);

            await pgcom.ExecuteNonQueryAsync();

            return SetLegacyId(item);
        }

        /// <inheritdoc/>
        public async Task<Application> Update(Application item)
        {
            SetInternalId(item);
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, item.Id);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, item);
            await pgcom.ExecuteNonQueryAsync();

            SetLegacyId(item);
            _memoryCache.Set(item.Id, item, _cacheEntryOptionsMetadata);
            return item;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string appId, string org)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, appId.Replace('/', '-'));

            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, string>> GetAllAppTitles()
        {
            if (!_memoryCache.TryGetValue(_cacheKey, out Dictionary<string, string> appTitles))
            {
                appTitles = new Dictionary<string, string>();
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

                    appTitles.Add(item.Id, titles.ToString());
                }

                _memoryCache.Set(_cacheKey, appTitles, _cacheEntryOptionsTitles);
            }

            return appTitles;
        }

        private static Application SetLegacyId(Application app)
        {
            if (app.Id.StartsWith(app.Org + '-', StringComparison.OrdinalIgnoreCase))
            {
                string appIdWithoutOrg = app.Id[(app.Org.Length + 1)..];
                app.Id = $"{app.Org}/{appIdWithoutOrg}";
            }

            return app;
        }

        private static void SetInternalId(Application app)
        {
            if (app.Id.StartsWith(app.Org + '/', StringComparison.OrdinalIgnoreCase))
            {
                string appIdWithoutOrg = app.Id[(app.Org.Length + 1)..];
                app.Id = $"{app.Org}-{appIdWithoutOrg}";
            }
        }
    }
}
