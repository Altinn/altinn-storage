using System.Diagnostics.CodeAnalysis;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// Configuration object used to hold settings for the file scan queue.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class FileScanQueueSettings
    {
        /// <summary>
        /// The connection string for the storage account with the queue.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Name of the queue for malware scanning.
        /// </summary>
        public string FileScanQueueName { get; set; }
    }
}
