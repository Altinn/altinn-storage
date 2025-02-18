using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to applications.
    /// </summary>
    public class ApplicationService : IApplicationService
    {
        private readonly IApplicationRepository _applicationRepository;
        private readonly ILogger<ApplicationService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationService"/> class.
        /// </summary>
        public ApplicationService(IApplicationRepository applicationRepository, ILogger<ApplicationService> logger)
        {
            _applicationRepository = applicationRepository;
            _logger = logger;
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
        public async Task<(Application Application, ServiceError ServiceError)> GetApplicationOrErrorAsync(string appId)
        {
            ServiceError serviceError = null;
            Application appInfo = null;

            try
            {
                string org = appId.Split("/")[0];
                appInfo = await _applicationRepository.FindOne(appId, org);

                if (appInfo == null)
                {
                    serviceError = new ServiceError(404, $"Did not find application with appId={appId}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An error occurred while fetching application with appId={appId}", appId);
                serviceError = new ServiceError(500, $"An error occurred while fetching application with appId={appId}");
            }

            return (appInfo, serviceError);
        }
    }
}
