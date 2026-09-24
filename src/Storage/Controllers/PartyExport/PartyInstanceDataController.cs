#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.OpenApi;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers.PartyExport;

/// <summary>
/// Serves the content of the data elements of a party, one file for each request.
/// </summary>
[Route("storage/api/v1/parties/{partyId:int}/instances/{instanceGuid:guid}/data")]
[ApiController]
[ExcludeFromPublicStorageApi]
[Tags("PartyExport")]
public class PartyInstanceDataController(IDataElementContentService dataElementContentService)
    : ControllerBase
{
    /// <summary>
    /// Gets the content of one data element, with its stored content type. The endpoint does not
    /// return a data element that is marked for permanent deletion.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner.</param>
    /// <param name="userId">The user id of the person that the export is for.</param>
    /// <param name="authenticationLevel">The authentication level to use for the authorization decisions of the person.</param>
    /// <param name="instanceGuid">The id of the instance the data element belongs to.</param>
    /// <param name="dataGuid">The id of the data element.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The data file as a stream.</returns>
    /// <remarks>
    /// An export is not a read operation by the instance owner. Thus, the endpoint does not mark
    /// the data element as read.
    /// </remarks>
    [HttpGet("{dataGuid:guid}")]
    [Authorize(Policy = AuthzConstants.POLICY_SCOPE_DATA_SUPPORTDASHBOARD)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(
        [FromRoute] int partyId,
        [FromHeader(Name = StorageHeaders.UserId), Required] int? userId,
        [FromHeader(Name = StorageHeaders.AuthenticationLevel), Required] int? authenticationLevel,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
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

        (DataElementReadContext context, ServiceError resolveError) =
            await dataElementContentService.ResolveForReadForUser(
                partyId,
                instanceGuid,
                dataGuid,
                new UserSubject(userId.Value, authenticationLevel.Value),
                cancellationToken
            );
        if (resolveError is not null)
        {
            return ErrorResult(resolveError);
        }

        InstanceInternal instance = context.Instance;
        DataElementInternal dataElement = context.DataElement;

        if (dataElement.DeleteStatus?.IsHardDeleted == true)
        {
            return NotFound();
        }

        Stream dataStream = await dataElementContentService.OpenContent(
            context,
            LanguageHelper.GetCurrentUserLanguage(Request),
            cancellationToken
        );

        if (context.IsOnDemandContent)
        {
            if (dataStream is null)
            {
                return NotFound();
            }

            Response.SetInlineContentDisposition(dataElement.Filename);
            return File(dataStream, dataElement.ContentType);
        }

        if (dataStream is null)
        {
            return NotFound($"Unable to read data element from blob storage for {dataGuid}");
        }

        Response.SetBlobVersionETag(dataElement.BlobVersionId);

        // Migrated Altinn 2 Websa main forms should be shown inline in the browser
        if (
            instance.AppId.Contains(@"/a2-")
            && dataElement.DataType == "ref-data-as-pdf"
            && dataElement.ContentType == "text/html"
        )
        {
            Response.SetInlineContentDisposition(dataElement.Filename);
            return File(dataStream, dataElement.ContentType);
        }

        return File(dataStream, dataElement.ContentType, dataElement.Filename);
    }

    private ActionResult ErrorResult(ServiceError serviceError) =>
        serviceError.ErrorCode switch
        {
            400 => BadRequest(serviceError.ErrorMessage),
            403 => Forbid(),
            404 => NotFound(serviceError.ErrorMessage),
            _ => StatusCode(serviceError.ErrorCode, serviceError.ErrorMessage),
        };
}
