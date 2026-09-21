using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.OpenApi;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Controllers.PartyExport;

/// <summary>
/// Exports the instances of an instance owner in batches. Each instance includes its data
/// elements.
/// </summary>
[Route("storage/api/v1/parties/{partyId:int}/instances")]
[ApiController]
[ExcludeFromPublicStorageApi]
[Tags("PartyExport")]
public class PartyInstancesController(
    IInstanceRepository instanceRepository,
    IOptions<GeneralSettings> settings,
    ILogger<PartyInstancesController> logger
) : ControllerBase
{
    private const int _defaultPageSize = 50;
    private const int _maxPageSize = 100;

    private readonly GeneralSettings _generalSettings = settings.Value;

    /// <summary>
    /// Gets the instances that a party owns. Each instance includes its data elements. The oldest
    /// instance is first, by the time of creation. A change to an instance does not change this
    /// sequence. Thus, if you follow the next links to the last batch, you get all the instances
    /// that the party had when the export started. The endpoint does not return an item that is
    /// marked for permanent deletion.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner.</param>
    /// <param name="size">The maximum number of instances in one batch. The default value is 50. The maximum value is 100.</param>
    /// <param name="dateFrom">The oldest date to include. If you give no value, there is no lower limit.</param>
    /// <param name="dateTo">The newest date to include. If you give no value, there is no upper limit.</param>
    /// <param name="continuationToken">The token from the previous batch. If you give no value, the batch starts at the oldest instance.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>A batch of instances owned by the party.</returns>
    /// <remarks>
    /// The response does not contain self links. A self link refers to an instance-scoped
    /// endpoint, and an export scope cannot use those endpoints. Thus, the client must build the
    /// party-scoped route to the data element from the ids. The date limits are inclusive. A date
    /// limit keeps an instance if the time of creation or the time of the last change is in the
    /// range. If the caller gives no time offset, the system reads the date as UTC. For example,
    /// <c>dateTo=2026-09-18</c> is midnight at the start of that day.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = AuthzConstants.POLICY_SCOPE_INSTANCES_SUPPORTDASHBOARD)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [Produces("application/json")]
    public async Task<ActionResult<QueryResponse<Instance>>> GetInstancesForParty(
        [FromRoute] int partyId,
        [FromQuery] int? size,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken
    )
    {
        if (partyId <= 0)
        {
            return BadRequest("The party id must be a positive number.");
        }

        if (size is < 1 or > _maxPageSize)
        {
            return BadRequest($"The size must be between 1 and {_maxPageSize}.");
        }

        DateTime? from = DateTimeHelper.ConvertToUniversalTime(dateFrom);
        DateTime? to = DateTimeHelper.ConvertToUniversalTime(dateTo);

        if (from > to)
        {
            return BadRequest("The dateFrom must not be later than the dateTo.");
        }

        InstanceContinuationToken? continueFrom = null;
        if (!string.IsNullOrEmpty(continuationToken))
        {
            if (
                !InstanceContinuationToken.TryParse(
                    continuationToken,
                    out InstanceContinuationToken parsed
                )
            )
            {
                return BadRequest("The continuation token is not valid.");
            }

            continueFrom = parsed;
        }

        InstanceQueryResult result = await instanceRepository.GetInstancesForParty(
            partyId,
            size ?? _defaultPageSize,
            from,
            to,
            continueFrom,
            cancellationToken
        );

        if (!string.IsNullOrEmpty(result.Exception))
        {
            logger.LogError(
                "Unable to retrieve instances for party {PartyId}: {Exception}",
                partyId,
                result.Exception
            );
            return StatusCode(
                cancellationToken.IsCancellationRequested ? 499 : 500,
                result.Exception
            );
        }

        List<Instance> instances = [.. result.Instances.Select(instance => instance.ToApiModel())];

        QueryResponse<Instance> response = new()
        {
            Instances = instances,
            Count = instances.Count,
            Self = Request.BuildContinuationLink(_generalSettings.Hostname, continuationToken),
        };

        if (!string.IsNullOrEmpty(result.ContinuationToken))
        {
            response.Next = Request.BuildContinuationLink(
                _generalSettings.Hostname,
                result.ContinuationToken
            );
        }

        return Ok(response);
    }
}
