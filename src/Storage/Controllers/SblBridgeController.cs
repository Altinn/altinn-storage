using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// API for use by Correspondence to support legacy solution by routing request to SBL Bridge
    /// </summary>
    public class SblBridgeController : ControllerBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SblBridgeController"/> class
        /// </summary>
        public SblBridgeController(IPartiesWithInstancesClient partiesWithInstancesClient)
        {
            _partiesWithInstancesClient = partiesWithInstancesClient;
        }

        private readonly IPartiesWithInstancesClient _partiesWithInstancesClient;

        /// <summary>
        /// Endpoint to register Altinn 3 Correspondence recipient in SBL Bridge
        /// </summary>
        /// <param name="partyId">The party id that has become an Altinn 3 Correspondence recipient</param>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult> RegisterAltinn3CorrespondenceRecipient(int partyId)
        {        
            if (partyId <= 0)
            {
                return BadRequest("PartyId must be larger than null");
            }

            await _partiesWithInstancesClient.SetHasAltinn3Correspondence(partyId);    
            return Ok();
        }        
    }
}
