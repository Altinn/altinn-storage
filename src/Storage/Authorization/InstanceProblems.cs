using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// The problem details that instance authorization answers with.
/// </summary>
internal static class InstanceProblems
{
    /// <summary>
    /// 403: the caller is not authorized for the action. Retrying will not help.
    /// </summary>
    internal static ProblemDetails Forbidden() =>
        new()
        {
            Status = StatusCodes.Status403Forbidden,
            Type = "instance_forbidden",
            Title = "Forbidden",
        };

    /// <summary>
    /// 503: Altinn Authorization gave no decision, so it is unknown whether the caller is authorized.
    /// The caller may retry.
    /// </summary>
    internal static ProblemDetails AuthorizationUnavailable() =>
        new()
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Type = "authorization_unavailable",
            Title = "Authorization unavailable",
            Detail = "Altinn Authorization gave no decision. The request can be retried.",
        };

    /// <summary>
    /// 404: the caller is authorized, but the instance does not exist.
    /// </summary>
    internal static ProblemDetails NotFound(int instanceOwnerPartyId, Guid instanceGuid) =>
        new()
        {
            Status = StatusCodes.Status404NotFound,
            Type = "instance_not_found",
            Title = "Instance not found",
            Detail = $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}.",
        };
}
