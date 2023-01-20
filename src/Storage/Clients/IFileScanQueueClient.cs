using System.Threading;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// This interface describes the public interface of a file scan queue client implementation.
    /// </summary>
    public interface IFileScanQueueClient
    {
        /// <summary>
        /// Put the content of the given string on the File Scan queue.
        /// </summary>
        /// <param name="content">The content of the message to be put on the queue.</param>
        /// <param name="ct">A cancellation token should the request be cancelled.</param>
        /// <returns>A task representing the asynconous call to the queue service.</returns>
        Task EnqueueFileScan(string content, CancellationToken ct);
    }
}
