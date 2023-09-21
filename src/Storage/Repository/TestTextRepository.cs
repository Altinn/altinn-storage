using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
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
    /// Handles text repository.
    /// </summary>
    public class TestTextRepository : ITestTextRepository
    {
        private readonly ITextRepository _cosmosRepository;
        private readonly ITextRepository _postgresRepository;
        private readonly ILogger<PgTextRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestTextRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="telemetryClient">Telemetry client</param>
        /// <param name="cosmosRepository">The cosmos repository.</param>
        /// <param name="generalSettings">Settings</param>
        /// <param name="memoryCache">Memory cache</param>
        public TestTextRepository(
            ILogger<PgTextRepository> logger,
            NpgsqlDataSource dataSource,
            ITextRepository cosmosRepository,
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache,
            TelemetryClient telemetryClient)
        {
            _postgresRepository = new PgTextRepository(generalSettings, memoryCache, dataSource, telemetryClient);
            _cosmosRepository = cosmosRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<TextResource> Get(string org, string app, string language)
        {
            TextResource cosmosText = await _cosmosRepository.Get(org, app, language);
            TextResource postgresText = await _postgresRepository.Get(org, app, language);

            string postgresJson = JsonSerializer.Serialize(postgresText);
            string cosmosJson = JsonSerializer.Serialize(cosmosText);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgText: Diff in Get postgres data: {JsonSerializer.Serialize(postgresText, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgText: Diff in Get cosmos data: {JsonSerializer.Serialize(cosmosText, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgText: Diff in Get {org} {app} {language}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in Get {org} {app} {language}");
                }
            }

            return cosmosText;
        }

        /// <inheritdoc/>
        public async Task<List<TextResource>> Get(List<string> appIds, string language)
        {
            List<TextResource> cosmosTexts = await _cosmosRepository.Get(appIds, language);
            List<TextResource> postgresTexts = await _postgresRepository.Get(appIds, language);

            string postgresJson = JsonSerializer.Serialize(postgresTexts);
            string cosmosJson = JsonSerializer.Serialize(cosmosTexts);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgText: Diff in Get(list) postgres data: {JsonSerializer.Serialize(postgresTexts, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgText: Diff in Get(list) cosmos data: {JsonSerializer.Serialize(cosmosTexts, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgText: Diff in Get(list) {appIds.Count} {language}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in Get(list) {appIds.Count} {language}");
                }
            }

            return cosmosTexts;
        }

        /// <inheritdoc/>
        public async Task<TextResource> Create(string org, string app, TextResource textResource)
        {
            TextResource cosmosItem = await _cosmosRepository.Create(org, app, textResource);
            TextResource postgresItem = await _postgresRepository.Create(org, app, textResource);
            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<TextResource> Update(string org, string app, TextResource textResource)
        {
            TextResource itemKopi = JsonSerializer.Deserialize<TextResource>(JsonSerializer.Serialize(textResource));
            TextResource cosmosItem = await _cosmosRepository.Update(org, app, textResource);
            TextResource postgresItem = await _postgresRepository.Update(org, app, itemKopi);

            string cosmosJson = JsonSerializer.Serialize(cosmosItem);
            string postgresJson = JsonSerializer.Serialize(postgresItem);
            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgText: Diff in Update postgres data: {JsonSerializer.Serialize(postgresItem, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgText: Diff in Update cosmos data: {JsonSerializer.Serialize(cosmosItem, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgText: Diff in Update {textResource.Id} {org} {app}");
                _logger.LogError($"TestPgText: Diff in Update {textResource.Id} {org} {app}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in Update for {textResource.Id} {org} {app}");
                }
            }

            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string org, string app, string language)
        {
            bool cosmosDelete = await _cosmosRepository.Delete(org, app, language);
            bool postgresDelete = await _postgresRepository.Delete(org, app, language);
            if (cosmosDelete != postgresDelete)
            {
                _logger.LogError($"TestPgText: Diff in Delete {org} {app} {language}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in Delete for item {org} {app} {language}");
                }
            }

            return cosmosDelete;
        }
    }
}
