using System;
using System.IO;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository
{
    public class BlobRepositoryMock : IBlobRepository
    {
        public async Task<bool> DeleteBlob(string org, string blobStoragePath, int? alternateContainerNumber)
        {
            return await Task.FromResult(true);
        }

        public async Task<Stream> ReadBlob(string org, string blobStoragePath, int? alternateContainerNumber)
        {
            string dataPath = Path.Combine(GetDataBlobPath(), blobStoragePath);
            Stream fs = File.OpenRead(dataPath);

            return await Task.FromResult(fs);
        }

        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(string org, Stream stream, string blobStoragePath, int? alternateContainerNumber)
        {
            MemoryStream memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return (memoryStream.Length, DateTimeOffset.UtcNow);
        }

        private static string GetDataBlobPath()
        {
            string unitTestFolder = Path.GetDirectoryName(new Uri(typeof(DataRepositoryMock).Assembly.Location).LocalPath);
            return Path.Combine(unitTestFolder, "..", "..", "..", "data", "blob");
        }

        public Task<bool> DeleteDataBlobs(Instance instance, int? alternateContainerNumber)
        {
            throw new NotImplementedException();
        }
    }
}
