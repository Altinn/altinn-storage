using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for use by Correspondence to support legacy solution by routing request to SBL Bridge
/// </summary>
[Route("storage/api/v1/sblbridge")]
[ApiController]
public class SblBridgeController : ControllerBase
{
    private static readonly HashSet<string> _eventTypes = new(System.StringComparer.OrdinalIgnoreCase) { "read", "confirm", "delete" };

    /// <summary>
    /// Initializes a new instance of the <see cref="SblBridgeController"/> class
    /// </summary>
    public SblBridgeController(IPartiesWithInstancesClient partiesWithInstancesClient, ICorrespondenceClient correspondenceClient)
    {
        _partiesWithInstancesClient = partiesWithInstancesClient;
        _correspondenceClient = correspondenceClient;
    }

    private readonly IPartiesWithInstancesClient _partiesWithInstancesClient;
    private readonly ICorrespondenceClient _correspondenceClient;

    /// <summary>
    /// Endpoint to register Altinn 3 Correspondence recipient in SBL Bridge
    /// </summary>
    /// <param name="sblBridgeParty">The party that has become an Altinn 3 Correspondence recipient</param>
    [HttpPost("correspondencerecipient")]
    [Authorize(Policy = AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult> RegisterAltinn3CorrespondenceRecipient(
        [FromBody] SblBridgeParty sblBridgeParty)
    {
        if (sblBridgeParty.PartyId <= 0)
        {
            return BadRequest("PartyId must be larger than zero");
        }

        await _partiesWithInstancesClient.SetHasAltinn3Correspondence(sblBridgeParty.PartyId);
        return Ok();
    }

    /// <summary>
    /// Endpoint to sync an Altinn 3 Correspondence event to Altinn 2 if the Correspondence is originally an Altinn 2 Correspondence.
    /// </summary>
    /// <param name="correspondenceEventSync">The event to sync to Altinn 2.</param>
    [HttpPost("synccorrespondenceevent")]
    [Authorize(Policy = AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Produces("application/json")]
    public async Task<ActionResult> SyncAltinn3CorrespondenceEvent(
        [FromBody] CorrespondenceEventSync correspondenceEventSync)
    {
        if (correspondenceEventSync.CorrespondenceId <= 0)
        {
            return BadRequest("CorrespondenceId must be larger than zero");
        }
        else if (correspondenceEventSync.PartyId <= 0)
        {
            return BadRequest("PartyId must be larger than zero");
        }
        else if (correspondenceEventSync.EventTimeStamp == DateTimeOffset.MinValue)
        {
            return BadRequest("EventTimeStamp must have a valid value.");
        }

        string normalizedEventType = correspondenceEventSync.EventType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEventType) || !_eventTypes.Contains(normalizedEventType))
        {
            return BadRequest($"Invalid event type: {normalizedEventType} submitted. Valid values: read,confirm,delete.");
        }

        try
        {
            await _correspondenceClient.SyncCorrespondenceEvent(
                correspondenceEventSync.CorrespondenceId,
                correspondenceEventSync.PartyId,
                correspondenceEventSync.EventTimeStamp,
                normalizedEventType);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "SBL Bridge timed out.");
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "SBL Bridge call failed.");
        }
    }
}
