using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

namespace Altinn.Platform.Storage.CosmosBackup
{
    /// <summary>
    /// Azure Function class for handling tasks related to instance events.
    /// </summary>
    public static class InstanceEvents
    {
        /// <summary>
        /// Backs up Cosmos DB application documents in Blob Storage.
        /// </summary>
        /// <param name="input">DataElements document.</param>
        /// <param name="context">Function context.</param>
        /// <param name="log">Logger.</param>
        [FunctionName("InstanceEventsContainerBackup")]
        public static async Task InstanceEventsContainerBackup(
            [CosmosDBTrigger(
            databaseName: "Storage",
            containerName: "instanceEvents",
            Connection = "DBConnection",
            LeaseContainerName = "leases",
            LeaseContainerPrefix = "instanceEvents",
            CreateLeaseContainerIfNotExists = true)]IReadOnlyList<JObject> input,
            ExecutionContext context,
            ILogger log)
        {
            if (input != null && input.Count > 0)
            {
                IConfiguration config = ConfigHelper.LoadConfig(context);
                string blobName = string.Empty;

                foreach (JObject item in input)
                {
                    try
                    {
                        string id = (string)item["id"];
                        string partitionKey = (string)item["instanceId"];
                        blobName = $"{partitionKey}/{id}";

                        await BlobService.SaveBlob(config, $"instanceEvents/{blobName}", item.ToString());
                    }
                    catch (Exception e)
                    {
                        log.LogError($"Exception occured when storing element {blobName}. Exception: {e}. Message: {e.Message}");
                    }
                }
            }
        }
    }
}
