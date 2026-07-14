#nullable disable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Handles operations for signing all or a subset of dataelements for an instance
/// </summary>
[Route("storage/api/v1/instances")]
[ApiController]
public class SignController : ControllerBase
{
    private readonly ISigningService _signingService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignController"/> class
    /// </summary>
    /// <param name="signingService">An instance service with instance related business logic.</param>
    public SignController(ISigningService signingService)
    {
        _signingService = signingService;
    }

    /// <summary>
    /// Create signature document from listed data elements.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The guid of the instance.</param>
    /// <param name="signRequest">Sign request containing data element ids and sign status.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_SIGN)]
    [HttpPost("{instanceOwnerPartyId:int}/{instanceGuid:guid}/sign")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult> Sign(
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid,
        [FromBody] SignRequest signRequest,
        CancellationToken cancellationToken
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        if (
            string.IsNullOrEmpty(signRequest?.Signee?.UserId)
            && signRequest?.Signee?.SystemUserId is null
        )
        {
            return Problem(
                "The 'UserId' or 'SystemUserId' parameter must be defined for signee.",
                null,
                400
            );
        }

        var performedBy = User.GetUserOrOrgNo();
        if (string.IsNullOrEmpty(performedBy))
        {
            return Unauthorized();
        }

        SignDocumentCreateResult result = await _signingService.CreateSignDocument(
            instanceGuid,
            signRequest,
            performedBy,
            cancellationToken,
            preconditions.InstanceVersion,
            preconditions.ProcessStateVersion
        );

        if (result.Created)
        {
            StorageVersions versions =
                result.Versions
                ?? throw new UnreachableException(
                    "Created sign document result must include versions."
                );
            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, versions);
            return StatusCode(201, "SignDocument is created");
        }

        ServiceError serviceError =
            result.ServiceError
            ?? throw new UnreachableException("Failed sign document result must include an error.");
        if (serviceError.ErrorCode == StatusCodes.Status412PreconditionFailed)
        {
            StorageVersions versions =
                result.Versions
                ?? throw new UnreachableException(
                    "Precondition-failed sign document result must include versions."
                );
            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, versions);
            return StatusCode(
                StatusCodes.Status412PreconditionFailed,
                new ProblemDetails
                {
                    Status = StatusCodes.Status412PreconditionFailed,
                    Type = serviceError.ErrorMessage,
                    Title =
                        serviceError.ErrorMessage == "instance_version_mismatch"
                            ? "Instance version did not match expected version."
                            : "Process state version did not match expected version.",
                }
            );
        }

        return Problem(serviceError.ErrorMessage, null, serviceError.ErrorCode);
    }
}
