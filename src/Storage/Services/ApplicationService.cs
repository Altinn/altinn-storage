using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to applications.
    /// </summary>
    public class ApplicationService : IApplicationService
    {
        private readonly IApplicationRepository _applicationRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationService"/> class.
        /// </summary>
        public ApplicationService(IApplicationRepository applicationRepository)
        {
            _applicationRepository = applicationRepository;
        }

        /// <inheritdoc/>
        public async Task<(bool IsValid, ServiceError ServiceError)> ValidateDataTypeForApp(string org, string appId, string dataType, string currentTask)
        {
            Application application = await _applicationRepository.FindOne(appId, org);

            if (application == null)
            {
                return (false, new ServiceError(404, $"Cannot find application {appId} in storage"));
            }

            if (application.DataTypes.Exists(e => e.Id == dataType && (string.IsNullOrEmpty(e.TaskId) || e.TaskId == currentTask)))
            {
                return (true, null);
            }

            return (false, new ServiceError(405, $"DataType {dataType} is not declared in application metadata for app {appId}"));
        }

        /// <summary>
        /// Wrapper method for getting an application or an error message using the application id.
        /// </summary>
        /// <param name="appId">The application id</param>
        /// <returns></returns>
        public async Task<(Application Application, ActionResult ErrorMessage)> GetApplicationOrErrorAsync(string appId)
        {
            ActionResult errorResult = null;
            Application appInfo = null;

            try
            {
                string org = appId.Split("/")[0];

                appInfo = await _applicationRepository.FindOne(appId, org);

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
}
