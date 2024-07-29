using System.IO;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// Interface for ondemand access
    /// </summary>
    public interface IOndemandClient
    {
        /// <summary>
        /// Get ondemand data
        /// </summary>
        /// <param name="path">The path to access ondemand data</param>
        /// <returns>Nothing is returned.</returns>
        Task<Stream> GetStreamAsync(string path);
    }
}
