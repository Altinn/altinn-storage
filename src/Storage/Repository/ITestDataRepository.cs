using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;
using Azure.Storage.Blobs.Models;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Describes the implementation of a data element storage. 
    /// </summary>
    public interface ITestDataRepository : IDataRepository
    {
    }
}
