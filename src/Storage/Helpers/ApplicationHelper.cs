using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// A helper class for application related operations.
/// </summary>
public class ApplicationHelper(IApplicationRepository applicationRepository)
{
    /// <summary>
    /// Wrapper method for getting an application or an error message using the application id.
    /// </summary>
    /// <param name="appId">The application id</param>
    /// <returns></returns>
    internal async Task<(Application Application, ActionResult ErrorMessage)> GetApplicationOrErrorAsync(string appId)
    {
        ActionResult errorResult = null;
        Application appInfo = null;

        try
        {
            string org = appId.Split("/")[0];

            appInfo = await applicationRepository.FindOne(appId, org);

            if (appInfo == null)
            {
                errorResult = new NotFoundObjectResult($"Did not find application with appId={appId}");
            }
        }
        catch (Exception e)
        {
            errorResult = new ObjectResult($"Unable to perform request: {e}")
            {
                StatusCode = 500
            };
        }

        return (appInfo, errorResult);
    }
}
