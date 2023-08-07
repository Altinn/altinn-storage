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
        /// Initializes a new instance of the <see cref="TestInstanceRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
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
                _logger.LogError("TestPgInstance: Diff in Delete for item " + item.Id);
                throw new Exception("Diff in Delete for item " + item.Id);
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
                    _logger.LogError("TestPgInstance: Diff in GetInstancesFromQuery " + JsonSerializer.Serialize(queryParams));
                    throw new Exception("Diff in GetInstancesFromQuery");
                }
            }

            return returnCosmos && !postgresOnly ? cosmosResponse : postgresResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid, bool includeElements = true)
        {
            (Instance cosmosInstance, long cosmosInternalId) = await _cosmosRepository.GetOne(instanceOwnerPartyId, instanceGuid, includeElements);
            (Instance postgresInstance, long postgresInternalId) = await _postgresRepository.GetOne(instanceOwnerPartyId, instanceGuid, includeElements);

            string cosmosJson = JsonSerializer.Serialize(cosmosInstance);
            string postgresJson = JsonSerializer.Serialize(postgresInstance);
            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgInstance: Diff in GetOne for {instanceOwnerPartyId} {instanceGuid}");
                throw new Exception($"Diff in GetOne for {instanceOwnerPartyId} {instanceGuid}");
            }

            return (postgresInstance, postgresInternalId);
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance item)
        {
            Instance cosmosItem = await _cosmosRepository.Update(item);
            Instance postgresItem = await _postgresRepository.Update(item);

            string cosmosJson = JsonSerializer.Serialize(cosmosItem);
            string postgresJson = JsonSerializer.Serialize(postgresItem);
            if (cosmosJson != postgresJson)
            {
                _logger.LogError($"TestPgInstance: Diff in Update for {item.InstanceOwner.PartyId} {item.Id}");
                throw new Exception("Diff in Update");
            }

            return cosmosItem;
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
                    p_inst.Instances[x].Data = p_inst.Instances[x].Data.OrderBy(d => d.Id).ToList();
                    c_inst.Instances[x].Data = c_inst.Instances[x].Data.OrderBy(d => d.Id).ToList();

                    string px = JsonSerializer.Serialize(p_inst.Instances[x]);
                    string cx = JsonSerializer.Serialize(c_inst.Instances[x]);
                    if (!px.Equals(cx))
                    {
                        //string pxi = JsonSerializer.Serialize(p_inst.Instances[x], new JsonSerializerOptions() { WriteIndented = true });
                        //string cxi = JsonSerializer.Serialize(c_inst.Instances[x], new JsonSerializerOptions() { WriteIndented = true });
                        //Console.WriteLine("Diff in item " + x);
                        //Console.WriteLine(pxi);
                        //System.IO.File.WriteAllText(@"c:\temp\p.json", pxi);
                        //Console.WriteLine();
                        //Console.WriteLine(cxi);
                        //System.IO.File.WriteAllText(@"c:\temp\c.json", cxi);
                        isEqueal = false;
                        break;
                    }
                }             
            }

            //Console.WriteLine("CompareInstanceResponses " + isEqueal);
            return isEqueal;
        }
    }
}
