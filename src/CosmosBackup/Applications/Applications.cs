using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

namespace Altinn.Platform.Storage.CosmosBackup
{
    /// <summary>
    /// Azure Function class for handling tasks related to applications.
    /// </summary>
    public static class Applications
    {
        /// <summary>
        /// Backs up Cosmos DB application documents in Blob Storage.
        /// </summary>
        /// <param name="input">Application document.</param>
        /// <param name="context">Function context.</param>
        /// <param name="log">Logger.</param>
        [FunctionName("ApplicationsCollectionBackup")]
        public static async Task ApplicationsCollectionBackup(
                [CosmosDBTrigger(
                databaseName: "Storage",
                containerName: "applications",
                Connection = "DBConnection",
                LeaseContainerName = "leases",
                LeaseContainerPrefix = "applications",
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
                        string partitionKey = (string)item["org"];
                        blobName = $"{partitionKey}/{id}";

                        await BlobService.SaveBlob(config, $"applications/{blobName}", item.ToString());
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
