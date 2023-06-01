using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
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
    /// Handles text repository.
    /// </summary>
    public class PgTextRepository : ITextRepository, IHostedService
    {
        private static readonly string _readSql = "select textResource from storage.texts where org = $1 and app = $2 and language = $3";
        private static readonly string _readAppSql = "select id from storage.applications where alternateId = $1";
        private static readonly string _deleteSql = "delete from storage.texts where org = $1 and app = $2 and language = $3";
        private static readonly string _updateSql = "update storage.texts set textResource = $4 where org = $1 and app = $2 and language = $3";
        private static readonly string _createSql = "insert into storage.texts (org, app, language, textResource, applicationInternalId) values ($1, $2, $3, $4, $5)";

        private readonly ILogger<ITextRepository> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly MemoryCacheEntryOptions _cacheEntryOptions;
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgTextRepository"/> class
        /// </summary>
        /// <param name="generalSettings">the general settings</param>
        /// <param name="memoryCache">the memory cache</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgTextRepository(
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache,
            NpgsqlDataSource dataSource)
        {
            _memoryCache = memoryCache;
            _cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetPriority(CacheItemPriority.High)
                .SetAbsoluteExpiration(new TimeSpan(0, 0, generalSettings.Value.TextResourceCacheLifeTimeInSeconds));
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<TextResource> Get(string org, string app, string language)
        {
            ValidateArguments(org, app, language);
            string id = GetTextId(org, app, language);
            if (!_memoryCache.TryGetValue(id, out TextResource textResource))
            {
                    await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
                    pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
                    pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, app);
                    pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, language);
                    await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        textResource = PostProcess(org, app, language, reader.GetFieldValue<TextResource>("textResource"));
                        _memoryCache.Set(id, textResource, _cacheEntryOptions);
                    }
                    else
                    {
                        textResource = null;
                    }
            }

            return textResource;
        }

        /// <inheritdoc/>
        public async Task<List<TextResource>> Get(List<string> appIds, string language)
        {
            List<TextResource> result = new List<TextResource>();

            foreach (string appId in appIds)
            {
                string org = appId.Split("/")[0];
                string app = appId.Split("/")[1];

                // Swallowing exceptions, only adding valid text resources as this is used by messagebox
                try
                {
                    TextResource resource = await Get(org, app, language);
                    if (resource != null)
                    {
                        result.Add(resource);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error occured when retrieving text resources for {org}-{app} in language {language}.", org, app, language);
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<TextResource> Create(string org, string app, TextResource textResource)
        {
            ValidateArguments(org, app, textResource.Language);

            int applicationInternalId;
            await using NpgsqlCommand pgcomReadApp = _dataSource.CreateCommand(_readAppSql);
            pgcomReadApp.Parameters.AddWithValue(NpgsqlDbType.Text, $"{org}-{app}");
            await using NpgsqlDataReader reader = await pgcomReadApp.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                applicationInternalId = reader.GetFieldValue<int>("id");
            }
            else
            {
                throw new ArgumentException("App not found");
            }

            await using NpgsqlCommand pgcomRead = _dataSource.CreateCommand(_createSql);
            pgcomRead.Parameters.AddWithValue(NpgsqlDbType.Text, org);
            pgcomRead.Parameters.AddWithValue(NpgsqlDbType.Text, app);
            pgcomRead.Parameters.AddWithValue(NpgsqlDbType.Text, textResource.Language);
            pgcomRead.Parameters.AddWithValue(NpgsqlDbType.Jsonb, textResource);
            pgcomRead.Parameters.AddWithValue(NpgsqlDbType.Bigint, applicationInternalId);

            await pgcomRead.ExecuteNonQueryAsync();

            return PostProcess(org, app, textResource.Language, textResource);
        }

        /// <inheritdoc/>
        public async Task<TextResource> Update(string org, string app, TextResource textResource)
        {
            ValidateArguments(org, app, textResource.Language);

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, textResource.Language);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, textResource);

            var statusCode = await pgcom.ExecuteNonQueryAsync();

            // TODO: Should we throw an exception rather than returning null when "not found"
            return statusCode == 0 ? null : PostProcess(org, app, textResource.Language, textResource);
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string org, string app, string language)
        {
            ValidateArguments(org, app, language);

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, language);

            return await pgcom.ExecuteNonQueryAsync() == 1;
        }

        private static string GetTextId(string org, string app, string language)
        {
            return $"{org}-{app}-{language}";
        }

        /// <summary>
        /// Post processes the text resource. Creates id and adds partition key org
        /// </summary>
        private static TextResource PostProcess(string org, string app, string language, TextResource textResource)
        {
            textResource.Id = GetTextId(org, app, language);
            textResource.Org = org;
            return textResource;
        }

        /// <summary>
        /// Validates that org and app are not null, checks that language is two letter ISO string
        /// </summary>
        private static void ValidateArguments(string org, string app, string language)
        {
            if (string.IsNullOrEmpty(org))
            {
                throw new ArgumentException("Org can not be null or empty");
            }

            if (string.IsNullOrEmpty(app))
            {
                throw new ArgumentException("App can not be null or empty");
            }

            if (!LanguageHelper.IsTwoLetters(language))
            {
                throw new ArgumentException("Language must be a two letter ISO name");
            }
        }

        /// <inheritdoc/>
        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
