#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Instance authorization for controller actions. The methods hang off <see cref="ControllerBase"/>
/// because they produce controller concerns, HTTP problem details, rather than authorization results;
/// <see cref="IAuthorization"/> itself stays free of MVC types.
/// </summary>
internal static class ControllerAuthorizationExtensions
{
    /// <summary>
    /// Loads and authorizes an instance without its data elements.
    /// See <see cref="GetAuthorizedInstanceOrProblem(ControllerBase, IAuthorization, int, Guid, string, bool, CancellationToken)"/>.
    /// </summary>
    internal static Task<(
        InstanceInternal Instance,
        ProblemDetails Problem
    )> GetAuthorizedInstanceOrProblem(
        this ControllerBase controller,
        IAuthorization authorization,
        int instanceOwnerPartyId,
        Guid instanceGuid,
        string action,
        CancellationToken cancellationToken
    ) =>
        controller.GetAuthorizedInstanceOrProblem(
            authorization,
            instanceOwnerPartyId,
            instanceGuid,
            action,
            includeElements: false,
            cancellationToken
        );

    /// <summary>
    /// Loads and authorizes an instance for a controller action. Follows the controller guard
    /// convention: a null <c>Problem</c> means continue with <c>Instance</c>, a non-null <c>Problem</c>
    /// describes the response the action should return instead. The problem is 403 when the user is not
    /// authorized and 404 when the user is authorized but the instance does not exist, in that order, so
    /// that an unauthorized caller learns nothing about whether the instance exists.
    /// </summary>
    /// <param name="controller">The controller handling the request.</param>
    /// <param name="authorization">The authorization service.</param>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance.</param>
    /// <param name="action">The action to authorize, see <see cref="AuthorizationActions"/>.</param>
    /// <param name="includeElements">Whether to load the instance's data elements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<(
        InstanceInternal Instance,
        ProblemDetails Problem
    )> GetAuthorizedInstanceOrProblem(
        this ControllerBase controller,
        IAuthorization authorization,
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
            return (
                null,
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Type = "instance_forbidden",
                    Title = "Forbidden",
                }
            );
        }

        if (instance is null)
        {
            return (
                null,
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Type = "instance_not_found",
                    Title = "Instance not found",
                    Detail =
                        $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}.",
                }
            );
        }

        return (instance, null);
    }
}
