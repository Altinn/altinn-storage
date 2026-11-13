using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Instance metrics for the admin pages in Altinn Studio.
/// </summary>
[Route("storage/api/v1/studio/metrics")]
[ApiController]
public class StudioMetricsController(
    IStudioMetricsRepository studioMetricsRepository,
    ILogger<StudioMetricsController> logger
) : ControllerBase
{
    /// <summary>
    /// Gets instance metrics per app for a service owner and one UTC day.
    /// </summary>
    /// <param name="org">The service owner.</param>
    /// <param name="date">The UTC day, YYYY-MM-DD. Created, completed and time to complete describe this day; open counts describe the instances as they are now.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize(Policy = AuthzConstants.POLICY_STUDIO_DESIGNER)]
    [HttpGet("{org}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult<StudioDailyMetrics>> GetDailyMetrics(
        [FromRoute] string org,
        [FromQuery] DateOnly? date,
        CancellationToken ct
    )
    {
        if (date is null)
        {
            return BadRequest("date must be given as YYYY-MM-DD.");
        }

        try
        {
            return Ok(await studioMetricsRepository.GetDailyMetrics(org, date.Value, ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to read studio metrics for {Org} on {Date}", org, date);
            return StatusCode(
                ct.IsCancellationRequested ? 499 : 500,
                $"Unable to read studio metrics due to: {e.Message}"
            );
        }
    }
}
