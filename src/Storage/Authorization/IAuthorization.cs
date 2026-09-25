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
    /// Authorizes an action on a storage instance for a user who is not the caller.
    /// </summary>
    /// <param name="instance">The instance.</param>
    /// <param name="action">The action to authorize.</param>
    /// <param name="task">The process task for the action. If you give no value, the request does not contain a task.</param>
    /// <param name="subject">The user that the decision is for.</param>
    public Task<bool> AuthorizeInstanceActionForUser(
        InstanceInternal instance,
        string action,
        string task,
        UserSubject subject
    );

    /// <summary>
    /// Authorizes a read action on a storage instance with full process context.
    /// </summary>
    public Task<bool> AuthorizeEnrichedInstanceAction(InstanceInternal instance, string action);

    /// <summary>
    /// Authorizes an action on a storage instance with full process context, for a user who is
    /// not the caller.
    /// </summary>
    /// <param name="instance">The instance.</param>
    /// <param name="action">The action to authorize.</param>
    /// <param name="subject">The user that the decision is for.</param>
    public Task<bool> AuthorizeEnrichedInstanceActionForUser(
        InstanceInternal instance,
        string action,
        UserSubject subject
    );

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
    /// Authorizes storage instances for a user who is not the caller. Returns the instances that
    /// the user has the right to read.
    /// </summary>
    /// <param name="instances">The instances.</param>
    /// <param name="subject">The user that the decisions are for.</param>
    public Task<List<InstanceInternal>> AuthorizeInstancesForUser(
        List<InstanceInternal> instances,
        UserSubject subject
    );

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
