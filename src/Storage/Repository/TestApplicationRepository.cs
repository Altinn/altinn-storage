using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
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
    public class TestApplicationRepository : ITestApplicationRepository
    {
        private readonly IApplicationRepository _cosmosRepository;
        private readonly IApplicationRepository _postgresRepository;
        private readonly ILogger<PgApplicationRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestApplicationRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="cosmosRepository">The cosmos repository.</param>
        /// <param name="generalSettings">Settings</param>
        /// <param name="memoryCache">Memory cache</param>
        public TestApplicationRepository(
            ILogger<PgApplicationRepository> logger,
            NpgsqlDataSource dataSource,
            IApplicationRepository cosmosRepository,
            IOptions<GeneralSettings> generalSettings,
            IMemoryCache memoryCache)
        {
            _postgresRepository = new PgApplicationRepository(generalSettings, memoryCache, dataSource);
            _cosmosRepository = cosmosRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<List<Application>> FindAll()
        {
            List<Application> cosmosApps = await _cosmosRepository.FindAll();
            List<Application> postgresApps = await _postgresRepository.FindAll();

            string postgresJson = JsonSerializer.Serialize(postgresApps);
            string cosmosJson = JsonSerializer.Serialize(cosmosApps);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgApplication: Diff in FindAll postgres data: {JsonSerializer.Serialize(postgresApps, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgApplication: Diff in FindAll cosmos data: {JsonSerializer.Serialize(cosmosApps, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgApplication: Diff in FindAll");
                throw new Exception($"Diff in FindAll");
            }

            return cosmosApps;
        }

        /// <inheritdoc/>
        public async Task<List<Application>> FindByOrg(string org)
        {
            List<Application> cosmosApps = await _cosmosRepository.FindByOrg(org);
            List<Application> postgresApps = await _postgresRepository.FindByOrg(org);

            string postgresJson = JsonSerializer.Serialize(postgresApps);
            string cosmosJson = JsonSerializer.Serialize(cosmosApps);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgApplication: Diff in FindByOrg postgres data: {JsonSerializer.Serialize(postgresApps, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgApplication: Diff in FindByOrg cosmos data: {JsonSerializer.Serialize(cosmosApps, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgApplication: Diff in FindByOrg  {org}");
                throw new Exception($"Diff in FindByOrg {org}");
            }

            return cosmosApps;
        }

        /// <inheritdoc/>
        public async Task<Application> FindOne(string appId, string org)
        {
            Application cosmosApp = await _cosmosRepository.FindOne(appId, org);
            Application postgresApp = await _postgresRepository.FindOne(appId, org);

            string postgresJson = JsonSerializer.Serialize(postgresApp);
            string cosmosJson = JsonSerializer.Serialize(cosmosApp);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgApplication: Diff in FindOne postgres data: {JsonSerializer.Serialize(postgresApp, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgApplication: Diff in FindOne cosmos data: {JsonSerializer.Serialize(cosmosApp, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgApplication: Diff in FindOne {appId} {org}");
                throw new Exception($"Diff in FindOne {appId} {org}");
            }

            return cosmosApp;
        }

        /// <inheritdoc/>
        public async Task<Application> Create(Application item)
        {
            Application cosmosItem = await _cosmosRepository.Create(item);
            Application postgresItem = await _postgresRepository.Create(item);
            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<Application> Update(Application item)
        {
            Application itemKopi = JsonSerializer.Deserialize<Application>(JsonSerializer.Serialize(item));
            Application cosmosItem = await _cosmosRepository.Update(item);
            Application postgresItem = await _postgresRepository.Update(itemKopi);

            string cosmosJson = JsonSerializer.Serialize(cosmosItem);
            string postgresJson = JsonSerializer.Serialize(postgresItem);
            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgApplication: Diff in Update postgres data: {JsonSerializer.Serialize(postgresItem, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgApplication: Diff in Update cosmos data: {JsonSerializer.Serialize(cosmosItem, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgApplication: Diff in Update for {item.Id}");
                throw new Exception($"Diff in Update for {item.Id}");
            }

            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string appId, string org)
        {
            bool cosmosDelete = await _cosmosRepository.Delete(appId, org);
            bool postgresDelete = await _postgresRepository.Delete(appId, org);
            if (cosmosDelete != postgresDelete)
            {
                _logger.LogError($"TestPgApplication: Diff in Delete {appId} {org}");
                throw new Exception($"Diff in Delete for item {appId} {org}");
            }

            return cosmosDelete;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, string>> GetAllAppTitles()
        {
            Dictionary<string, string> cosmosTitles = await _cosmosRepository.GetAllAppTitles();
            Dictionary<string, string> postgresTitles = await _postgresRepository.GetAllAppTitles();

            string postgresJson = JsonSerializer.Serialize(postgresTitles);
            string cosmosJson = JsonSerializer.Serialize(cosmosTitles);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgApplication: Diff in GetAllAppTitles postgres data: {JsonSerializer.Serialize(postgresTitles, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgApplication: Diff in GetAllAppTitles cosmos data: {JsonSerializer.Serialize(cosmosTitles, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgApplication: Diff in GetAllAppTitles");
                throw new Exception($"Diff in GetAllAppTitles");
            }

            return cosmosTitles;
        }
    }
}
