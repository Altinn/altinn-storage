#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints specifically for altinn studio.
    /// </summary>
    [Route("storage/api/v1/studio/instances")]
    [ApiController]
    public class StudioInstancesController : ControllerBase
    {
        private readonly GeneralSettings _generalSettings;
        private readonly IInstanceRepository _instanceRepository;
        private readonly IAuthorization _authorizationService;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="StudioInstancesController"/> class
        /// </summary>
        /// <param name="settings">the general settings.</param>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="authorizationService">the authorization service</param>
        /// <param name="hostEnvironment">the host environment</param>
        /// <param name="logger">The logger</param>
        public StudioInstancesController(
            IOptions<GeneralSettings> settings,
            IInstanceRepository instanceRepository,
            IAuthorization authorizationService,
            IHostEnvironment hostEnvironment,
            ILogger<StudioInstancesController> logger)
        {
            _generalSettings = settings.Value;
            _instanceRepository = instanceRepository;
            _authorizationService = authorizationService;
            _hostEnvironment = hostEnvironment;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all (simplified) instances that match the specified query parameters. Parameters can be combined. Invalid or unknown parameter values will result in a 400 Bad Request response.
        /// </summary>
        /// <param name="parameters">The parameters to retrieve instance data.</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>A <seealso cref="List{T}"/> contains all instances for given instance owner.</returns>
        [Authorize]
        [HttpGet("{org}/{app}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult<QueryResponse<SimpleInstance>>> GetInstances(
            StudioInstanceParameters parameters,
            CancellationToken ct)
        {
            // This API is experimental and should not be available in production yet.
            if (_hostEnvironment.IsProduction())
            {
                return NotFound();
            }

            var orgClaim = User?.GetOrg();

            if (string.IsNullOrEmpty(orgClaim))
            {
                return Forbid();
            }

            if (!_authorizationService.UserHasRequiredScope(_generalSettings.InstanceReadScope))
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(parameters.Org) || string.IsNullOrEmpty(parameters.App))
            {
                return BadRequest("Org and App must be defined.");
            }

            if (!orgClaim.Equals(parameters.Org, StringComparison.InvariantCultureIgnoreCase))
            {
                return Forbid();
            }

            if (!string.IsNullOrEmpty(parameters.ContinuationToken))
            {
                parameters.ContinuationToken = HttpUtility.UrlDecode(parameters.ContinuationToken);
            }

            parameters.Size ??= 10;

            try
            {
                InstanceQueryResponse result = await _instanceRepository.GetInstancesFromQuery(
                    parameters.ToInstanceQueryParameters(),
                    false,
                    ct);

                if (!string.IsNullOrEmpty(result.Exception))
                {
                    _logger.LogError(
                        "Unable to perform query on instances: {Exception}",
                        result.Exception);
                    return StatusCode(ct.IsCancellationRequested ? 499 : 500, result.Exception);
                }

                string nextContinuationToken = HttpUtility.UrlEncode(result.ContinuationToken);

                QueryResponse<SimpleInstance> response = new()
                {
                    Instances = result
                        .Instances.Select(i => SimpleInstance.FromInstance(i))
                        .ToList(),
                    Count = result.Instances.Count,
                    Next = nextContinuationToken,
                };

                return Ok(response);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to perform query on instances");
                return StatusCode(
                    ct.IsCancellationRequested ? 499 : 500,
                    $"Unable to perform query on instances due to: {e.Message}");
            }
        }
    }
}
