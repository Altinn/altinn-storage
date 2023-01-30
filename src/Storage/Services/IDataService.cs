using System;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// This interface describes the required methods and features of a data service implementation.
    /// </summary>
    public interface IDataService
    {
        /// <summary>
        /// Trigger malware scan of the blob associated with the given data element.
        /// </summary>
        /// <param name="instance">The metadata document for the parent instance for the data element.</param>
        /// <param name="dataType">
        /// The data type properties document for the data type of the blob to be scanned for malware.
        /// </param>
        /// <param name="dataElement">The data element metadata document.</param>
        /// <param name="blobTimestamp">Timestamp when blob upload completed.</param>
        /// <param name="ct">A cancellation token should the request be cancelled.</param>
        /// <returns>A task representing the asynconous call to file scan service.</returns>
        Task StartFileScan(Instance instance, DataType dataType, DataElement dataElement, DateTimeOffset blobTimestamp, CancellationToken ct);
    }
}