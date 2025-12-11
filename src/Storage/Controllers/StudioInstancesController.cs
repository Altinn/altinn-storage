#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Implements endpoints specifically for altinn studio.
/// </summary>
[Route("storage/api/v1/studio/instances")]
[ApiController]
public class StudioInstancesController : ControllerBase
{
    private readonly IInstanceRepository _instanceRepository;
    private readonly GeneralSettings _generalSettings;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioInstancesController"/> class
    /// </summary>
    public StudioInstancesController(
        IInstanceRepository instanceRepository,
        IOptions<GeneralSettings> generalSettings,
        ILogger<StudioInstancesController> logger
    )
    {
        _instanceRepository = instanceRepository;
        _generalSettings = generalSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all (simplified) instances that match the specified query parameters. Parameters can be combined. Invalid or unknown parameter values will result in a 400 Bad Request response.
    /// </summary>
    /// <param name="parameters">The parameters to retrieve instance data.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>A <seealso cref="List{T}"/> contains all instances for given instance owner.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_STUDIO_DESIGNER)]
    [HttpGet("{org}/{app}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult<QueryResponse<SimpleInstance>>> GetInstances(
        StudioInstanceParameters parameters,
        CancellationToken ct
    )
    {
        // This API is experimental and should not be available in production or other service owners yet.
        if (
            !_generalSettings.Hostname.Contains("tt02", StringComparison.InvariantCultureIgnoreCase)
            || !_generalSettings.StudioInstancesOrgWhiteList.Contains(parameters.Org)
        )
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(parameters.Org) || string.IsNullOrEmpty(parameters.App))
        {
            return BadRequest("Org and App must be defined.");
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
                ct
            );

            if (!string.IsNullOrEmpty(result.Exception))
            {
                _logger.LogError(
                    "Unable to perform query on instances: {Exception}",
                    result.Exception
                );
                return StatusCode(ct.IsCancellationRequested ? 499 : 500, result.Exception);
            }

            string nextContinuationToken = HttpUtility.UrlEncode(result.ContinuationToken);

            QueryResponse<SimpleInstance> response = new()
            {
                Instances = result.Instances?.Select(SimpleInstance.FromInstance).ToList() ?? new(),
                Count = result.Instances?.Count ?? 0,
                Next = nextContinuationToken,
            };

            return Ok(response);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to perform query on instances");
            return StatusCode(
                ct.IsCancellationRequested ? 499 : 500,
                $"Unable to perform query on instances due to: {e.Message}"
            );
        }
    }

    /// <summary>
    /// Gets a specific instance with the given instance id.
    /// </summary>
    /// <param name="org">The org owning the the instance to retrieve.</param>
    /// <param name="app">The app tied to the instance to retrieve.</param>
    /// <param name="instanceGuid">The id of the instance to retrieve.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>Details about the specific instance</returns>
    [Authorize(Policy = AuthzConstants.POLICY_STUDIO_DESIGNER)]
    [HttpGet("{org}/{app}/{instanceGuid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<ActionResult<SimpleInstanceDetails>> Get(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        CancellationToken ct
    )
    {
        // This API is experimental and should not be available in production or other service owners yet.
        if (
            !_generalSettings.Hostname.Contains("tt02", StringComparison.InvariantCultureIgnoreCase)
            || !_generalSettings.StudioInstancesOrgWhiteList.Contains(org)
        )
        {
            return NotFound();
        }

        try
        {
            (var result, _) = await _instanceRepository.GetOne(instanceGuid, true, ct);
            if (result == null || result.Org != org || result.AppId != $"{org}/{app}")
            {
                return NotFound();
            }

            return Ok(SimpleInstanceDetails.FromInstance(result));
        }
        catch (Exception e)
        {
            return NotFound($"Unable to find instance {instanceGuid}: {e}");
        }
    }
}
