using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Services;
using AltinnCore.Authentication.Constants;
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
        private readonly IInstanceService _instanceService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SignController"/> class
        /// </summary>
        /// <param name="instanceService">A instance service with instance related business logic.</param>
        public SignController(IInstanceService instanceService)
        {
            _instanceService = instanceService;
        }

        /// <summary>
        /// Create signature document from listed data elements
        /// </summary>
        /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
        /// <param name="instanceGuid">The guid of the instance</param>
        /// <param name="signRequest">Signrequest containing data element ids and sign status</param>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_SIGN)]
        [HttpPost("{instanceOwnerPartyId:int}/{instanceGuid:guid}/sign")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult> Sign([FromRoute] int instanceOwnerPartyId, [FromRoute] Guid instanceGuid, [FromBody] SignRequest signRequest)
        {
            UserContext userContext = GetUserContext(User);
            (bool isValid, string errorMessage) = ValidateClaims(userContext);
            if (!isValid)
            {
                return Problem(errorMessage, null, 400);
            }

            await _instanceService.CreateSignDocument(instanceOwnerPartyId, instanceGuid, signRequest, userContext);
            return StatusCode(201, "SignDocument is created");
        }

        private UserContext GetUserContext(ClaimsPrincipal principal)
        {
            UserContext userContext = new UserContext
            {
                UserId = User.GetUserIdAsInt(),
                PartyId = User.GetPartyId(),
                Orgnr = User.GetOrgNumber()
            };
            return userContext;
        }

        private static (bool IsValid, string ErrorMessage) ValidateClaims(UserContext user)
        {
            if (user.UserId == null)
            {
                return (false, "The 'UserId' parameter must be defined in context.");
            }
            if (string.IsNullOrEmpty(user.PartyId))
            {
                return (false, "The 'PartyId' parameter must be defined in context.");
            }

            return (true, null);
        }
    }
}