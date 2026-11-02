using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

/// <summary>
/// A storage account repository without rows, so every account resolves to the pattern default.
/// </summary>
public class StorageAccountRepositoryMock : IStorageAccountRepository
{
    public Task<IReadOnlyList<StorageAccountRow>> ReadAll(
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<StorageAccountRow>>([]);

    public Task<short> Insert(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
}
