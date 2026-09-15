#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Implements endpoints on demand content generation
/// </summary>
[Route(
    "storage/api/v1/ondemand/{org}/{app}/{instanceOwnerPartyId:int}/{instanceGuid:guid}/{dataGuid:guid}/{language}"
)]
[ApiExplorerSettings(IgnoreApi = true)]
[ApiController]
public class ContentOnDemandController(IOnDemandContentService onDemandContentService)
    : ControllerBase
{
    /// <summary>
    /// Gets the formatted content
    /// </summary>
    /// <param name="org">org</param>
    /// <param name="app">app</param>
    /// <param name="instanceGuid">instanceGuid</param>
    /// <param name="dataGuid">dataGuid</param>
    /// <param name="language">language</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The formatted content</returns>
    [HttpGet("signature")]
    public async Task<ActionResult> GetSignatureAsHtml(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        [FromRoute] string language,
        CancellationToken cancellationToken
    )
    {
        Stream html = await onDemandContentService.GetSignatureAsHtml(
            instanceGuid,
            cancellationToken
        );

        return html is null ? NotFound() : File(html, "text/html");
    }

    /// <summary>
    /// Gets the formatted content
    /// </summary>
    /// <param name="org">org</param>
    /// <param name="app">app</param>
    /// <param name="instanceGuid">instanceGuid</param>
    /// <param name="dataGuid">dataGuid</param>
    /// <param name="language">language</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The formatted content</returns>
    [HttpGet("payment")]
    public async Task<ActionResult> GetPaymentAsHtml(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        [FromRoute] string language,
        CancellationToken cancellationToken
    )
    {
        Stream html = await onDemandContentService.GetPaymentAsHtml(
            instanceGuid,
            cancellationToken
        );

        return html is null ? NotFound() : File(html, "text/html");
    }

    /// <summary>
    /// Gets the formatted content
    /// </summary>
    /// <param name="org">org</param>
    /// <param name="app">app</param>
    /// <param name="instanceGuid">instanceGuid</param>
    /// <param name="dataGuid">dataGuid</param>
    /// <param name="language">language</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The formatted content</returns>
    [HttpGet("formdatapdf")]
    public async Task<ActionResult<Stream>> GetFormdataAsPdf(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        [FromRoute] string language,
        CancellationToken cancellationToken
    )
    {
        Stream pdf = await onDemandContentService.GetFormdataAsPdf(
            app,
            instanceGuid,
            dataGuid,
            language,
            cancellationToken
        );
        if (pdf is null)
        {
            return NotFound();
        }

        return pdf;
    }

    /// <summary>
    /// Gets the formatted content
    /// </summary>
    /// <param name="org">org</param>
    /// <param name="app">app</param>
    /// <param name="instanceGuid">instanceGuid</param>
    /// <param name="dataGuid">dataGuid</param>
    /// <param name="language">language</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="singlePageNr">optional filter for a single page number</param>
    /// <returns>The formatted content</returns>
    [HttpGet("formdatahtml/{singlepagenr?}")]
    public async Task<ActionResult<Stream>> GetFormdataAsHtml(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        [FromRoute] string language,
        CancellationToken cancellationToken,
        [FromRoute(Name = "singlepagenr")] int singlePageNr = -1
    )
    {
        Stream html = await onDemandContentService.GetFormdataAsHtml(
            app,
            instanceGuid,
            dataGuid,
            language,
            cancellationToken,
            singlePageNr
        );
        if (html is null)
        {
            return NotFound();
        }

        return html;
    }

    /// <summary>
    /// Gets the formatted content
    /// </summary>
    /// <param name="org">org</param>
    /// <param name="app">app</param>
    /// <param name="instanceGuid">instanceGuid</param>
    /// <param name="dataGuid">dataGuid</param>
    /// <param name="language">language</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The formatted content</returns>
    [HttpGet("formsummaryhtml")]
    public async Task<ActionResult<Stream>> GetFormSummaryAsHtml(
        [FromRoute] string org,
        [FromRoute] string app,
        [FromRoute] Guid instanceGuid,
        [FromRoute] Guid dataGuid,
        [FromRoute] string language,
        CancellationToken cancellationToken
    )
    {
        Stream html = await onDemandContentService.GetFormSummaryAsHtml(
            app,
            instanceGuid,
            dataGuid,
            language,
            cancellationToken
        );
        if (html is null)
        {
            return NotFound();
        }

        return html;
    }
}
