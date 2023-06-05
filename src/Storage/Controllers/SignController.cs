using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Handles operations for signing all or a subset of dataelements for an instance
    /// </summary>
    [Route("storage/api/v1/instances")]
    [ApiController]
    public class SignController : ControllerBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SignController"/> class
        /// </summary>
        public SignController()
        {
        }

        /// <summary>
        /// Create signature document from listed data elements
        /// </summary>
        /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
        /// <param name="instanceGuid">The id of the instance</param>
        /// <param name="signRequest">Signrequest containing data element ids and sign status</param>
        [Authorize(Policy = "Sign")]
        [HttpPost("{instanceOwnerPartyId:int}/{instanceGuid:guid}/sign")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult> Sign([FromRoute] int instanceOwnerPartyId, [FromRoute] Guid instanceGuid, [FromBody] SignRequest signRequest)
        {
            return StatusCode(201, "SignDocument is created");
        }
    }
}