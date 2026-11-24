using System;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ABAC.Xacml;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// The problem details that authorization answers with, and how a controller returns them.
/// </summary>
internal static class AuthorizationProblems
{
    private static readonly string _indeterminate = XacmlContextDecision.Indeterminate.ToString();

    /// <summary>
    /// Whether Altinn Authorization reached a decision. It answers <c>Indeterminate</c> when it fails
    /// while evaluating the request, so that is treated like no response at all.
    /// </summary>
    internal static bool HasDecision([NotNullWhen(true)] XacmlJsonResponse? response) =>
        response?.Response is not null
        && !response.Response.Exists(result => result.Decision == _indeterminate);

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

    /// <summary>
    /// The action result that answers with <paramref name="problem"/> and its status code.
    /// </summary>
    internal static ObjectResult ToResult(this ProblemDetails problem) =>
        new(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
}
