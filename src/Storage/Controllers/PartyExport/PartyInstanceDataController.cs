#nullable disable

using System;
using System.Globalization;
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
/// Serves the content of a party's data elements, one file per request, for exporting everything a
/// party holds.
/// </summary>
[Route("storage/api/v1/parties/{partyId:int}/instances/{instanceGuid:guid}/data")]
[ApiController]
[ExcludeFromPublicStorageApi]
[Tags("PartyExport")]
public class PartyInstanceDataController(IDataElementContentService dataElementContentService)
    : ControllerBase
{
    /// <summary>
    /// Gets the content of a single data element. The content type is the same as the file was
    /// stored with. Anything awaiting permanent deletion is left out, matching the instances the export walk returns.
    /// </summary>
    /// <param name="partyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance the data element belongs to.</param>
    /// <param name="dataGuid">The id of the data element to retrieve.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The data file as a stream.</returns>
    /// <remarks>
    /// Unlike the instance-scoped download this does not mark the data element as read: an export
    /// is not the instance owner reading their message.
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
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        if (partyId <= 0)
        {
            return BadRequest("The party id must be a positive number.");
        }

        (DataElementReadContext context, ServiceError resolveError) =
            await dataElementContentService.ResolveForRead(
                partyId,
                instanceGuid,
                dataGuid,
                cancellationToken
            );
        if (resolveError is not null)
        {
            return ErrorResult(resolveError);
        }

        InstanceInternal instance = context.Instance;
        DataElementInternal dataElement = context.DataElement;

        if (
            !string.Equals(
                instance.InstanceOwner?.PartyId,
                partyId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            )
        )
        {
            return NotFound($"Unable to find any instance with id: {partyId}/{instanceGuid}.");
        }

        if (dataElement.DeleteStatus?.IsHardDeleted == true)
        {
            return NotFound();
        }

        if (context.IsOnDemandContent)
        {
            Response.SetInlineContentDisposition(dataElement.Filename);
        }

        Stream dataStream = await dataElementContentService.OpenContent(
            context,
            LanguageHelper.GetCurrentUserLanguage(Request),
            cancellationToken
        );

        if (context.IsOnDemandContent)
        {
            return dataStream is null ? NotFound() : File(dataStream, dataElement.ContentType);
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
