﻿using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Authorization
{
    /// <summary>
    /// Interface for the authorization service
    /// </summary>
    public interface IAuthorization
    {
        /// <summary>
        /// Authorize instances, and returns a list of MesseageBoxInstances with information about read and write rights of each instance.
        /// </summary>
        public Task<List<MessageBoxInstance>> AuthorizeMesseageBoxInstances(List<Instance> instances, bool includeInstantiate);

        /// <summary>
        /// Authorizes a given action on an instance.
        /// </summary>
        /// <returns>true if the user is authorized.</returns>
        public Task<bool> AuthorizeInstanceAction(Instance instance, string action, string task = null);
        
        /// <summary>
        /// Authorizes that the user has one or more of the actions on an instance.
        /// </summary>
        /// <returns>true if the user is authorized.</returns>
        public Task<bool> AuthorizeAnyOfInstanceActions(Instance instance, List<string> actions);

        /// <summary>
        /// Authorize instances, and returns a list of instances that the user has the right to read.
        /// </summary>
        public Task<List<Instance>> AuthorizeInstances(List<Instance> instances);

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
}
