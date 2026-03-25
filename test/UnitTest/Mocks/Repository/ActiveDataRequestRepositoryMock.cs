#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

/// <summary>
/// Mock implementation of <see cref="IActiveDataRequestRepository"/> that always allows mutations.
/// </summary>
public class ActiveDataRequestRepositoryMock : IActiveDataRequestRepository
{
    private long _nextRequestId = 1;

    public Task<(BeginMutationStatus Status, long? RequestId)> BeginDataMutation(
        Guid instanceGuid,
        LockToken? lockToken,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        return Task.FromResult<(BeginMutationStatus, long?)>((BeginMutationStatus.Ok, requestId));
    }

    public Task EndDataMutation(long requestId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
