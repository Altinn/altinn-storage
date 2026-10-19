#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Controller-facing conveniences over <see cref="IAuthorization"/>.
/// </summary>
internal static class AuthorizationExtensions
{
    /// <summary>
    /// Loads and authorizes an instance for a controller action, mapping the outcome to the response the
    /// action should return. Follows the controller guard convention: a null <c>Error</c> means continue
    /// with <c>Instance</c>, a non-null <c>Error</c> is returned immediately. The error is
    /// <see cref="ForbidResult"/> when the user is not authorized and <see cref="NotFoundObjectResult"/>
    /// when the user is authorized but the instance does not exist, in that order, so that an unauthorized
    /// caller learns nothing about whether the instance exists.
    /// </summary>
    /// <param name="authorization">The authorization service.</param>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance.</param>
    /// <param name="action">The action to authorize, see <see cref="AuthorizationActions"/>.</param>
    /// <param name="includeElements">Whether to load the instance's data elements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<(
        InstanceInternal Instance,
        ActionResult Error
    )> GetAuthorizedInstanceOrError(
        this IAuthorization authorization,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        string action,
        bool includeElements,
        CancellationToken cancellationToken
    )
    {
        (InstanceInternal instance, bool isAuthorized) = await authorization.GetAuthorizedInstance(
            instanceOwnerPartyId,
            instanceGuid,
            action,
            includeElements,
            cancellationToken
        );

        if (!isAuthorized)
        {
            return (null, new ForbidResult());
        }

        if (instance is null)
        {
            return (
                null,
                new NotFoundObjectResult(
                    $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}."
                )
            );
        }

        return (instance, null);
    }
}
