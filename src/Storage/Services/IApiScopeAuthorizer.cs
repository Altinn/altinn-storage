using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Authorizes access to application instances based on current request context token scopes.
/// </summary>
public interface IApiScopeAuthorizer
{
    /// <summary>
    /// Checks if the current request context token has access to the given instance.
    /// </summary>
    /// <param name="operation">operation</param>
    /// <param name="appId">appId of format 'org/app'</param>
    /// <returns></returns>
    Task<bool> Authorize(InstanceOperation operation, string appId);

    /// <summary>
    /// Checks if the current request context token has access to the given instance.
    /// </summary>
    /// <param name="operation">operation</param>
    /// <param name="instance">instance</param>
    /// <returns></returns>
    Task<bool> Authorize(InstanceOperation operation, Instance instance);
}
