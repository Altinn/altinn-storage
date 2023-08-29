using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="ITestInstanceEventRepository"/>.
    /// </summary>
    public class TestInstanceEventRepository: ITestInstanceEventRepository
    {
        private readonly IInstanceEventRepository _cosmosRepository;
        private readonly IInstanceEventRepository _postgresRepository;
        private readonly ILogger<PgInstanceEventRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestInstanceEventRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="cosmosRepository">The cosmos repository.</param>
        public TestInstanceEventRepository(
            ILogger<PgInstanceEventRepository> logger,
            NpgsqlDataSource dataSource,
            IInstanceEventRepository cosmosRepository)
        {
            _postgresRepository = new PgInstanceEventRepository(dataSource);
            _cosmosRepository = cosmosRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> InsertInstanceEvent(InstanceEvent instanceEvent)
        {
            InstanceEvent cosmosItem = await _cosmosRepository.InsertInstanceEvent(instanceEvent);
            InstanceEvent postgresItem = await _postgresRepository.InsertInstanceEvent(instanceEvent);
            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<InstanceEvent> GetOneEvent(string instanceId, Guid eventGuid)
        {
            InstanceEvent cosmosEvent = await _cosmosRepository.GetOneEvent(instanceId, eventGuid);
            InstanceEvent postgresEvent = await _postgresRepository.GetOneEvent(instanceId, eventGuid);

            string postgresJson = JsonSerializer.Serialize(postgresEvent);
            string cosmosJson = JsonSerializer.Serialize(cosmosEvent);

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgInstanceEvent: Diff in GetOneEvent postgres data: {JsonSerializer.Serialize(postgresEvent, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgInstanceEvent: Diff in GetOneEvent cosmos data: {JsonSerializer.Serialize(cosmosEvent, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgInstanceEvent: Diff in GetOneEvent for {instanceId} {eventGuid}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in GetOne for {instanceId} {eventGuid}");
                }
            }

            return cosmosEvent;
        }

        /// <inheritdoc/>
        public async Task<List<InstanceEvent>> ListInstanceEvents(
            string instanceId,
            string[] eventTypes,
            DateTime? fromDateTime,
            DateTime? toDateTime)
        {
            List<InstanceEvent> cosmosEvents = null;
            List<InstanceEvent> postgresEvents = null;

            string postgresJson = null;
            string cosmosJson = null;
            for (int i = 0; i < 10; i++)
            {
                cosmosEvents = await _cosmosRepository.ListInstanceEvents(instanceId, eventTypes, fromDateTime, toDateTime);
                postgresEvents = await _postgresRepository.ListInstanceEvents(instanceId, eventTypes, fromDateTime, toDateTime);
                postgresJson = JsonSerializer.Serialize(postgresEvents);
                cosmosJson = JsonSerializer.Serialize(cosmosEvents);

                if (cosmosJson == postgresJson)
                {
                    break;
                }

                _logger.LogError($"TracePgListInstanceEvents: {instanceId} c:{cosmosEvents.Count} p:{postgresEvents.Count}");

                await Task.Delay(50);
            }

            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgInstanceEvent: Diff in ListInstanceEvents postgres data: {JsonSerializer.Serialize(postgresEvents, new JsonSerializerOptions() { WriteIndented = true })}");
                _logger.LogError($"TestPgInstanceEvent: Diff in ListInstanceEvents cosmos data: {JsonSerializer.Serialize(cosmosEvents, new JsonSerializerOptions() { WriteIndented = true })}");

                _logger.LogError($"TestPgInstanceEvent: Diff in ListInstanceEvents for {instanceId}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"Diff in ListInstanceEvents for {instanceId}");
                }
            }

            return cosmosEvents;
        }

        /// <inheritdoc/>
        public async Task<int> DeleteAllInstanceEvents(string instanceId)
        {
            int cosmosDelete = await _cosmosRepository.DeleteAllInstanceEvents(instanceId);
            int postgresDelete = await _postgresRepository.DeleteAllInstanceEvents(instanceId);
            if (cosmosDelete != postgresDelete)
            {
                _logger.LogError($"TestPgInstanceEvent: Diff in DeleteAllInstanceEvents for id {instanceId} c:{cosmosDelete} p:{postgresDelete}");
                if (TestInstanceRepository.AbortOnError)
                {
                    throw new Exception($"TestPgInstanceEvent: Diff in DeleteAllInstanceEvents for id {instanceId} c:{cosmosDelete} p:{postgresDelete}");
                }
            }

            return cosmosDelete;
        }
    }
}
