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

namespace Altinn.Platform.Storage.Controllers
{
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
        /// <summary>
        /// Initializes a new instance of <see cref="SblBridgeController"/> with the required clients.
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
        /// <summary>
        /// Registers a party as an Altinn 3 correspondence recipient.
        /// </summary>
        /// <param name="sblBridgeParty">Object containing the party identifier to mark as an Altinn 3 correspondence recipient. The PartyId must be &gt; 0.</param>
        /// <returns>200 OK when the party was registered; 400 Bad Request if the provided PartyId is not larger than zero.</returns>
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
        /// <summary>
        /// Synchronizes a correspondence event from Altinn 3 (SBL Bridge) into Altinn 2.
        /// </summary>
        /// <param name="correspondenceEventSync">The correspondence event to sync; must contain valid CorrespondenceId (&gt;0), PartyId (&gt;0), a non-minimum EventTimeStamp, and an EventType of "read", "confirm", or "delete".</param>
        /// <returns>
        /// HTTP 200 on success.
        /// HTTP 400 when input validation fails (invalid ids, timestamp, or event type).
        /// HTTP 504 if the SBL Bridge request times out.
        /// HTTP 502 if the SBL Bridge request fails with an HTTP error.
        /// </returns>
        [HttpPost("synccorrespondenceevent")]
        [Authorize(Policy = AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
                return BadRequest($"Invalid event type: {correspondenceEventSync.EventType} submitted. Valid values: read,confirm,delete.");
            }

            try
            {
                await _correspondenceClient.SyncCorrespondenceEvent(
                    correspondenceEventSync.CorrespondenceId,
                    correspondenceEventSync.PartyId,
                    correspondenceEventSync.EventTimeStamp,
                    correspondenceEventSync.EventType);
                return Ok();
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
}
