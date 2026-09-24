using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
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
/// Exports the instances of a party in batches.
/// </summary>
[Route("storage/api/v1/parties/{partyId:int}/instances")]
[ApiController]
[ExcludeFromPublicStorageApi]
[Tags("PartyExport")]
public class PartyInstancesController(
    IInstanceRepository instanceRepository,
    IAuthorization authorizationService,
    IOptions<GeneralSettings> settings,
    ILogger<PartyInstancesController> logger
) : ControllerBase
{
    private const int _defaultPageSize = 50;
    private const int _maxPageSize = 100;

    private readonly GeneralSettings _generalSettings = settings.Value;

    /// <summary>
    /// Gets the instances that a party owns, with their data elements. The oldest instance is
    /// first. Follow the next links until there is no next link. The endpoint does not return
    /// an instance that is marked for permanent deletion.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner.</param>
    /// <param name="userId">The user id of the person that the export is for.</param>
    /// <param name="authenticationLevel">The authentication level to use for the authorization decisions of the person.</param>
    /// <param name="size">The maximum number of instances in one batch.</param>
    /// <param name="dateFrom">The oldest date to include.</param>
    /// <param name="dateTo">The newest date to include.</param>
    /// <param name="continuationToken">The token from the previous batch.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>A batch of instances owned by the party.</returns>
    /// <remarks>
    /// A batch contains only the instances that the person can read. Thus a batch can have
    /// fewer instances than the size, or none, and still have a next link.
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
        [FromHeader(Name = StorageHeaders.UserId), Required] int? userId,
        [FromHeader(Name = StorageHeaders.AuthenticationLevel), Required] int? authenticationLevel,
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

        if (userId is not > 0)
        {
            return BadRequest($"The {StorageHeaders.UserId} header must be a positive number.");
        }

        if (authenticationLevel is not >= 0)
        {
            return BadRequest(
                $"The {StorageHeaders.AuthenticationLevel} header must be zero or a positive number."
            );
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

        List<InstanceInternal> authorized = await authorizationService.AuthorizeInstancesForUser(
            result.Instances,
            new UserSubject(userId.Value, authenticationLevel.Value)
        );

        HashSet<Guid> authorizedIds = [.. authorized.Select(instance => instance.Id)];
        List<Instance> instances =
        [
            .. result
                .Instances.Where(instance => authorizedIds.Contains(instance.Id))
                .Select(instance => instance.ToApiModel()),
        ];

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
