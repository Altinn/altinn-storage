#nullable disable

using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// This interface describes the required methods and features of a application service implementation.
/// </summary>
public interface IApplicationService
{
    /// <summary>
    /// Check if a datatype is valid for given app
    /// </summary>
    /// <param name="application">The application metadata to validate against.</param>
    /// <param name="dataType">The data type identifier for the data being uploaded.</param>
    /// <param name="currentTask">The task info of the currentTask of an ongoing process.</param>
    /// <returns>Result of validation. If the result (IsValid) is false, it will be described in ServiceError</returns>
    (bool IsValid, ServiceError ServiceError) ValidateDataTypeForApp(
        Application application,
        string dataType,
        string currentTask
    );

    /// <summary>
    /// Get application or error message using the application id.
    /// </summary>
    /// <param name="appId">The id of the application.</param>
    /// <returns> Result of the operation. If application is null, the reason/error will be described in ServiceError</returns>
    Task<(Application Application, ServiceError ServiceError)> GetApplicationOrErrorAsync(
        string appId
    );
}
