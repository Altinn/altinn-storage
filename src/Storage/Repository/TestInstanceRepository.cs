using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IInstanceRepository"/>.
    /// </summary>
    public class TestInstanceRepository: ITestInstanceRepository
    {
        private readonly IInstanceRepository _cosmosRepository;
        private readonly IInstanceRepository _postgresRepository;
        private readonly ILogger<PgInstanceRepository> _logger;

        /// <summary>
        /// Whether to ignore diffs in filescanresult
        /// </summary>
        public static bool IgnoreFileScan { get; set; } = true;

        /// <summary>
        /// Whether to abort on error
        /// </summary>
        public static bool AbortOnError { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestInstanceRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        /// <param name="cosmosRepository">The cosmos repository.</param>
        public TestInstanceRepository(
            ILogger<PgInstanceRepository> logger,
            NpgsqlDataSource dataSource,
            IInstanceRepository cosmosRepository)
        {
            _postgresRepository = new PgInstanceRepository(logger, dataSource);
            _cosmosRepository = cosmosRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Instance> Create(Instance item)
        {
            Instance cosmosItem = await _cosmosRepository.Create(item);
            Instance postgresItem = await _postgresRepository.Create(item);
            return cosmosItem;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(Instance item)
        {
            bool cosmosDelete = await _cosmosRepository.Delete(item);
            bool postgresDelete = await _postgresRepository.Delete(item);
            if (cosmosDelete != postgresDelete)
            {
                _logger.LogError($"TestPgInstance: Diff in Delete for item {item.Id} c:{cosmosDelete} p:{postgresDelete}");
                if (AbortOnError)
                {
                    throw new Exception($"TestPgInstance: Diff in Delete for item {item.Id} c:{cosmosDelete} p:{postgresDelete}");
                }
            }

            return cosmosDelete;
        }

        /// <inheritdoc/>
        public async Task<InstanceQueryResponse> GetInstancesFromQuery(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            bool returnCosmos = true;
            if (!string.IsNullOrEmpty(continuationToken) && continuationToken.IndexOf("usePostgresToken", StringComparison.OrdinalIgnoreCase) != -1)
            {
                returnCosmos = false;
                continuationToken = null;
            }

            bool postgresOnly = !string.IsNullOrEmpty(continuationToken) && continuationToken.IndexOf("token", StringComparison.OrdinalIgnoreCase) == -1;
            bool cosmosOnly = !string.IsNullOrEmpty(continuationToken) && continuationToken.IndexOf("token", StringComparison.OrdinalIgnoreCase) != -1;

            Stopwatch swp = Stopwatch.StartNew();
            InstanceQueryResponse postgresResponse = cosmosOnly ? null : await _postgresRepository.GetInstancesFromQuery(queryParams, continuationToken, size);
            swp.Stop();
            Console.WriteLine("pg tid: " + swp.ElapsedMilliseconds);
            Stopwatch swc = Stopwatch.StartNew();
            InstanceQueryResponse cosmosResponse = postgresOnly ? null : await _cosmosRepository.GetInstancesFromQuery(queryParams, continuationToken, size);
            swc.Stop();
            Console.WriteLine("co tid: " + swc.ElapsedMilliseconds + "\r\n");

            if (string.IsNullOrEmpty(continuationToken))
            {
                Console.WriteLine(postgresResponse.Count);
                Console.WriteLine(cosmosResponse.Count);
                if (!CompareInstanceResponses(postgresResponse, cosmosResponse))
                {
                    ////System.IO.File.WriteAllText(@"c:\temp\c.json", JsonSerializer.Serialize(cosmosResponse, new JsonSerializerOptions() { WriteIndented = true }));
                    ////System.IO.File.WriteAllText(@"c:\temp\p.json", JsonSerializer.Serialize(postgresResponse, new JsonSerializerOptions() { WriteIndented = true }));
                    _logger.LogError($"TestPgInstance: Diff in GetInstancesFromQuery postgres data: {JsonSerializer.Serialize(postgresResponse, new JsonSerializerOptions() { WriteIndented = true })}");
                    _logger.LogError($"TestPgInstance: Diff in GetInstancesFromQuery cosmos data: {JsonSerializer.Serialize(cosmosResponse, new JsonSerializerOptions() { WriteIndented = true })}");

                    _logger.LogError("TestPgInstance: Diff in GetInstancesFromQuery " + JsonSerializer.Serialize(queryParams));
                    if (AbortOnError)
                    {
                        throw new Exception("Diff in GetInstancesFromQuery");
                    }
                }
            }

            return returnCosmos && !postgresOnly ? cosmosResponse : postgresResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid, bool includeElements = true)
        {
            Instance cosmosInstance = null;
            long cosmosInternalId = 0;
            (Instance postgresInstance, long postgresInternalId) = await _postgresRepository.GetOne(instanceOwnerPartyId, instanceGuid, includeElements);

            string postgresJson = JsonSerializer.Serialize(postgresInstance);
            string cosmosJson = null;
            for (int i = 0; i < 4; i++)
            {
                (cosmosInstance, cosmosInternalId) = await _cosmosRepository.GetOne(instanceOwnerPartyId, instanceGuid, includeElements);
                cosmosJson = JsonSerializer.Serialize(cosmosInstance);
                Instance cosmosInstancePatched = cosmosInstance;
                if (!includeElements && cosmosInstance.Data != null)
                {
                    cosmosInstancePatched = JsonSerializer.Deserialize<Instance>(cosmosJson);
                    cosmosInstancePatched.Data = new();
                    cosmosInstancePatched.LastChanged = postgresInstance.LastChanged;
                    cosmosInstancePatched.LastChangedBy = postgresInstance.LastChangedBy;
                    cosmosJson = JsonSerializer.Serialize(cosmosInstancePatched);
                }

                if (cosmosJson == postgresJson)
                {
                    break;
                }

                await Task.Delay(50);
            }

            if (cosmosJson != postgresJson)
            {
                Instance patchedCosmos = JsonSerializer.Deserialize<Instance>(cosmosJson);
                Instance patchedPostgres = JsonSerializer.Deserialize<Instance>(postgresJson);
                SyncLastChanged(patchedCosmos, patchedPostgres);
                if (IgnoreFileScan)
                {
                    foreach (var data in patchedCosmos.Data)
                    {
                        data.FileScanResult = FileScanResult.Clean;
                    }

                    foreach (var data in patchedPostgres.Data)
                    {
                        data.FileScanResult = FileScanResult.Clean;
                    }
                }

                postgresJson = JsonSerializer.Serialize(patchedPostgres);
                cosmosJson = JsonSerializer.Serialize(patchedCosmos);

                if (cosmosJson != postgresJson)
                {
                    ////System.IO.File.WriteAllText(@"c:\temp\p.json", JsonSerializer.Serialize(postgresInstance, new JsonSerializerOptions() { WriteIndented = true }));
                    ////System.IO.File.WriteAllText(@"c:\temp\c.json", JsonSerializer.Serialize(cosmosInstance, new JsonSerializerOptions() { WriteIndented = true }));
                    _logger.LogError($"TestPgInstance: Diff in GetOne postgres data: {JsonSerializer.Serialize(postgresInstance, new JsonSerializerOptions() { WriteIndented = true })}");
                    _logger.LogError($"TestPgInstance: Diff in GetOne cosmos data: {JsonSerializer.Serialize(cosmosInstance, new JsonSerializerOptions() { WriteIndented = true })}");

                    _logger.LogError($"TestPgInstance: Diff in GetOne for {instanceOwnerPartyId} {instanceGuid}");
                    if (AbortOnError)
                    {
                        throw new Exception($"Diff in GetOne for {instanceOwnerPartyId} {instanceGuid}");
                    }
                }
            }

            return (postgresInstance, postgresInternalId);
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance item)
        {
            Instance itemKopi = JsonSerializer.Deserialize<Instance>(JsonSerializer.Serialize(item));
            Instance cosmosItem = await _cosmosRepository.Update(item);
            Instance postgresItem = await _postgresRepository.Update(itemKopi);

            string cosmosJson = JsonSerializer.Serialize(cosmosItem);
            string postgresJson = JsonSerializer.Serialize(postgresItem);
            if (cosmosJson != postgresJson)
            {
                Instance patchedCosmos = JsonSerializer.Deserialize<Instance>(cosmosJson);
                Instance patchedPostgres = JsonSerializer.Deserialize<Instance>(postgresJson);
                SyncLastChanged(patchedCosmos, patchedPostgres);
                if (IgnoreFileScan)
                {
                    foreach (var data in patchedCosmos.Data)
                    {
                        data.FileScanResult = FileScanResult.Clean;
                    }

                    foreach (var data in patchedPostgres.Data)
                    {
                        data.FileScanResult = FileScanResult.Clean;
                    }
                }

                if (patchedCosmos.Process.CurrentTask.Started != patchedPostgres.Process.CurrentTask.Started && Math.Abs(((DateTime)patchedCosmos.Process.CurrentTask.Started).Subtract((DateTime)patchedPostgres.Process.CurrentTask.Started).TotalSeconds) < 5)
                {
                    patchedPostgres.Process.CurrentTask = patchedCosmos.Process.CurrentTask;
                }

                postgresJson = JsonSerializer.Serialize(patchedPostgres);
                cosmosJson = JsonSerializer.Serialize(patchedCosmos);

                if (cosmosJson != postgresJson)
                {
                    _logger.LogError($"TestPgInstance: Diff in Update postgres data: {JsonSerializer.Serialize(postgresItem, new JsonSerializerOptions() { WriteIndented = true })}");
                    _logger.LogError($"TestPgInstance: Diff in Update cosmos data: {JsonSerializer.Serialize(cosmosItem, new JsonSerializerOptions() { WriteIndented = true })}");

                    _logger.LogError($"TestPgInstance: Diff in Update for {item.InstanceOwner.PartyId} {item.Id}");
                    if (AbortOnError)
                    {
                        throw new Exception("Diff in Update");
                    }
                }
            }

            return cosmosItem;
        }

        private void SyncLastChanged (Instance cosmosInstance, Instance postgresInstance)
        {
            if (Math.Abs(((DateTime)cosmosInstance.LastChanged).Subtract((DateTime)postgresInstance.LastChanged).TotalSeconds) < 5)
            {
                cosmosInstance.LastChanged = postgresInstance.LastChanged;
            }
        }

        private bool CompareInstanceResponses(InstanceQueryResponse p_inst, InstanceQueryResponse c_inst)
        {
            if (p_inst?.Instances?.Count == 0 && c_inst?.Instances?.Count == 0)
            {
                return true;
            }
            else if (p_inst?.Instances?.Count == 0 && c_inst?.Instances?.Count > 0)
            {
                Console.WriteLine("Postgres is 0");
                return false;
            }
            else if (p_inst?.Instances?.Count > 0 && c_inst?.Instances?.Count == 0)
            {
                Console.WriteLine("Cosmos is 0");
                return false;
            }

            string p_instString = JsonSerializer.Serialize(p_inst);
            string c_instString = JsonSerializer.Serialize(c_inst);
            InstanceQueryResponse p_patched = JsonSerializer.Deserialize<InstanceQueryResponse>(p_instString);
            InstanceQueryResponse c_patched = JsonSerializer.Deserialize<InstanceQueryResponse>(c_instString);
            p_patched.ContinuationToken = null;
            c_patched.ContinuationToken = null;
            p_instString = JsonSerializer.Serialize(p_patched);
            c_instString = JsonSerializer.Serialize(c_patched);
            bool isEqueal = p_instString.Equals(c_instString);

            if (!isEqueal)
            {
                isEqueal = true;
                for (int x = 0; x < p_inst.Count; x++)
                {
                    SyncLastChanged(c_inst.Instances[x], p_inst.Instances[x]);
                    p_inst.Instances[x].Data = p_inst.Instances[x].Data.OrderBy(d => d.Id).ToList();
                    c_inst.Instances[x].Data = c_inst.Instances[x].Data.OrderBy(d => d.Id).ToList();

                    string px = JsonSerializer.Serialize(p_inst.Instances[x]);
                    string cx = JsonSerializer.Serialize(c_inst.Instances[x]);
                    if (!px.Equals(cx))
                    {
                        ////string pxi = JsonSerializer.Serialize(p_inst.Instances[x], new JsonSerializerOptions() { WriteIndented = true });
                        ////string cxi = JsonSerializer.Serialize(c_inst.Instances[x], new JsonSerializerOptions() { WriteIndented = true });
                        ////Console.WriteLine("Diff in item " + x);
                        ////Console.WriteLine(pxi);
                        ////System.IO.File.WriteAllText(@"c:\temp\p.json", pxi);
                        ////Console.WriteLine();
                        ////Console.WriteLine(cxi);
                        ////System.IO.File.WriteAllText(@"c:\temp\c.json", cxi);

                        isEqueal = false;
                        break;
                    }
                }             
            }

            return isEqueal;
        }
    }
}
