#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Exceptions;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Tracks active data mutation requests to coordinate with instance locking.
/// Parses the lock token from the HTTP request header and delegates to
/// <see cref="IActiveDataRequestRepository"/> for the actual tracking.
/// </summary>
public sealed class DataMutationTracker(
    IActiveDataRequestRepository activeDataRequestRepository,
    IHttpContextAccessor httpContextAccessor,
    IOptions<GeneralSettings> generalSettings,
    ILogger<DataMutationTracker> logger
) : IDataMutationTracker
{
    private const string LockTokenHeader = "Altinn-Storage-Lock-Token";

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> BeginMutation(
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    )
    {
        var lockToken = ParseLockTokenFromRequest();
        var timeout = TimeSpan.FromSeconds(generalSettings.Value.ActiveDataRequestTimeoutSeconds);

        var (status, requestId) = await activeDataRequestRepository.BeginDataMutation(
            instanceGuid,
            lockToken,
            timeout,
            cancellationToken
        );

        if (status == BeginMutationStatus.MutationBlocked)
        {
            throw new MutationBlockedException();
        }

        if (status == BeginMutationStatus.InstanceNotFound)
        {
            return new NoOpHandle();
        }

        return new DataMutationHandle(activeDataRequestRepository, logger, requestId);
    }

    private LockToken? ParseLockTokenFromRequest()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        if (
            !httpContext.Request.Headers.TryGetValue(LockTokenHeader, out var lockTokenHeaderValues)
        )
        {
            return null;
        }

        var lockTokenHeaderValue = lockTokenHeaderValues.ToString();
        if (string.IsNullOrEmpty(lockTokenHeaderValue))
        {
            return null;
        }

        try
        {
            return LockToken.ParseToken(lockTokenHeaderValue);
        }
        catch (FormatException e)
        {
            logger.LogWarning(e, "Could not parse lock token from header.");
            return null;
        }
    }

    private sealed class NoOpHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DataMutationHandle(
        IActiveDataRequestRepository activeDataRequestRepository,
        ILogger logger,
        long? requestId
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (!requestId.HasValue)
            {
                return;
            }

            try
            {
                await activeDataRequestRepository.EndDataMutation(requestId.Value, default);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to end data mutation tracking for request {RequestId}.",
                    requestId.Value
                );
            }
        }
    }
}
