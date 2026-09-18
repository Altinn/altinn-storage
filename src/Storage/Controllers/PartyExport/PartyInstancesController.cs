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
/// Exports the instances of an instance owner, data elements included, in batches.
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
    /// Retrieves the instances owned by a party, each with its data elements, oldest first by
    /// creation time. Later changes never reorder instances, so following the next links to the
    /// end yields every instance the party held when the walk started. Anything awaiting
    /// permanent deletion is left out.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner.</param>
    /// <param name="size">The maximum number of instances in one batch. Defaults to 50, at most 100.</param>
    /// <param name="dateFrom">The oldest date to include. Omit it for no lower bound.</param>
    /// <param name="dateTo">The newest date to include. Omit it for no upper bound.</param>
    /// <param name="continuationToken">The token from the previous batch. Omit it to start at the oldest instance.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>A batch of instances owned by the party.</returns>
    /// <remarks>
    /// Self links are not set. They address the instance-scoped endpoints, which an export scope
    /// does not reach, so a consumer builds the party-scoped data element route from the ids.
    /// The date bounds are inclusive, keep an instance whose creation or last changed time falls
    /// inside them, and are read as UTC when the caller supplies no offset, so
    /// <c>dateTo=2026-09-18</c> means midnight that morning.
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
