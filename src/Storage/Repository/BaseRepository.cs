namespace Altinn.Platform.Storage.Repository
{
    using System.Threading;
    using System.Threading.Tasks;

    using Altinn.Platform.Storage.Configuration;

    using Microsoft.Azure.Cosmos;

    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Base repository service for initializing db, collections etc.
    /// </summary>
    internal abstract class BaseRepository : IHostedService
    {
        private readonly string _databaseId;
        private readonly string _collectionId;
        private readonly string _partitionKey;
        private readonly CosmosClient _cosmosClient;

        /// <summary>
        /// The document collection.
        /// </summary>
        protected Container Container { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseRepository"/> class.
        /// </summary>
        /// <param name="collectionId">The ID of the collection.</param>
        /// <param name="partitionKey">The PK of the collection.</param>
        /// <param name="cosmosSettings">The settings object.</param>
        /// <param name="cosmosClient">CosmosClient singleton</param>
        public BaseRepository(
            string collectionId, 
            string partitionKey, 
            IOptions<AzureCosmosSettings> cosmosSettings, 
            CosmosClient cosmosClient)
        {
            _collectionId = collectionId;
            _partitionKey = partitionKey;
            _databaseId = cosmosSettings.Value.Database;
            _cosmosClient = cosmosClient;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Container = await CreateDatabaseAndCollection(cancellationToken);
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task<Container> CreateDatabaseAndCollection(CancellationToken cancellationToken)
        {
            Database db = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseId, cancellationToken: cancellationToken);

            Container container = await db.CreateContainerIfNotExistsAsync(_collectionId, _partitionKey, cancellationToken: cancellationToken);
            await SetIndexPolicy(container);

            return container;
        }

        /// <summary>
        /// Programmatically set index policy.
        /// </summary>
        protected virtual async Task SetIndexPolicy(Container container)
        {
            var containerResponse = await container.ReadContainerAsync();

            var newPolicy = new IndexingPolicy();
            newPolicy.IndexingMode = IndexingMode.Consistent;
            newPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
            newPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/_etag/?" });

            containerResponse.Resource.IndexingPolicy = newPolicy;

            var result = await container.ReplaceContainerAsync(containerResponse.Resource);
            Container = result.Container;
        }
    }
}
