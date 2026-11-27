#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class BlobRepositoryMock : IBlobRepository
{
    public async Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath)
    {
        return await Task.FromResult(true);
    }

    public Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    )
    {
        bool[] result = new bool[blobStoragePaths.Count];
        Array.Fill(result, true);

        return Task.FromResult(result);
    }

    public async Task<Stream> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        string dataPath = Path.Combine(GetDataBlobPath(), blobStoragePath);
        Stream fs = File.OpenRead(dataPath);

        return await Task.FromResult(fs);
    }

    public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    )
    {
        MemoryStream memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return (memoryStream.Length, DateTimeOffset.UtcNow);
    }

    private static string GetDataBlobPath()
    {
        string unitTestFolder = Path.GetDirectoryName(
            new Uri(typeof(DataRepositoryMock).Assembly.Location).LocalPath
        );
        return Path.Combine(unitTestFolder, "..", "..", "..", "data", "blob");
    }

    public Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public Task<(Uri Url, DateTimeOffset ExpiresOn)> GetReadUrl(
        StorageAccount account,
        string blobStoragePath,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            (
                new Uri($"https://blob.example/{blobStoragePath}?sig=test"),
                DateTimeOffset.UtcNow.Add(lifetime)
            )
        );
}
