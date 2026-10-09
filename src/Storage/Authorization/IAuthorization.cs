#nullable disable

using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Interface for the authorization service
/// </summary>
public interface IAuthorization
{
    /// <summary>
    /// Authorize instances, and returns a list of MesseageBoxInstances with information about read and write rights of each instance.
    /// </summary>
    public Task<List<MessageBoxInstance>> AuthorizeMesseageBoxInstances(
        List<InstanceInternal> instances,
        bool keyAccessMode
    );

    /// <summary>
    /// Authorizes a given action on a storage instance.
    /// </summary>
    public Task<bool> AuthorizeInstanceAction(
        InstanceInternal instance,
        string action,
        string task = null
    );

    /// <summary>
    /// Authorizes the current HTTP request to perform <paramref name="action"/> on the instance
    /// identified by the request's route values. Callers with the sync adapter scope are authorized
    /// directly for read/write/delete without contacting the PDP. When <paramref name="instance"/>
    /// is provided the XACML request is enriched with the instance's process context (current task
    /// or end event) and the decision is cached. Without an instance the request is denied unless
    /// the route identifies one.
    /// </summary>
    /// <param name="instance">The instance to authorize against, or null when no instance exist.</param>
    /// <param name="action">The action to authorize, e.g. "read", "write" or "delete".</param>
    /// <returns>true if the user is authorized.</returns>
    public Task<bool> AuthorizeInstanceRequest(InstanceInternal instance, string action);

    /// <summary>
    /// Authorizes that the user has one or more of the actions on a storage instance.
    /// </summary>
    public Task<bool> AuthorizeAnyOfInstanceActions(
        InstanceInternal instance,
        List<string> actions
    );

    /// <summary>
    /// Authorize storage instances, and returns the instances that the user has the right to read.
    /// </summary>
    public Task<List<InstanceInternal>> AuthorizeInstances(List<InstanceInternal> instances);

    /// <summary>
    /// Verifies that the user has at least one of the supplied scopes.
    /// </summary>
    /// <param name="requiredScope">Required scopes</param>
    /// <returns>true if the current user has any of the scopes provided.</returns>
    public bool UserHasRequiredScope(List<string> requiredScope);

    /// <summary>
    /// Verifies that the user has the supplied scope.
    /// </summary>
    /// <param name="requiredScope">Required scopes</param>
    /// <returns>true if the current user has the scope provided.</returns>
    public bool UserHasRequiredScope(string requiredScope);

    /// <summary>
    /// Sends in a request and get response with result of the request
    /// </summary>
    /// <param name="xacmlJsonRequest">The Xacml Json Request</param>
    /// <returns>The Xacml Json response contains the result of the request</returns>
    public Task<XacmlJsonResponse> GetDecisionForRequest(XacmlJsonRequestRoot xacmlJsonRequest);
}
