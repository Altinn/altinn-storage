#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Generates the presentation content for migrated Altinn 2 data elements, which is rendered per
/// request rather than stored as a blob. The kind of content is encoded in the data element's
/// blob storage path, as <c>ondemand/&lt;kind&gt;</c>.
/// </summary>
public interface IOnDemandContentService
{
    /// <summary>
    /// Generates the content for the given kind, or <c>null</c> when the kind is unknown or the
    /// instance it refers to no longer exists.
    /// </summary>
    /// <param name="kind">The kind of content, taken from the data element's blob storage path.</param>
    /// <param name="app">The app the instance belongs to, without the org prefix.</param>
    /// <param name="instanceGuid">The instance holding the data element.</param>
    /// <param name="dataGuid">The data element to generate content for.</param>
    /// <param name="language">The language to generate the content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<Stream> GetContent(
        string kind,
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Generates the signing information for an instance as HTML.
    /// </summary>
    /// <param name="instanceGuid">The instance to generate content for.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<Stream> GetSignatureAsHtml(Guid instanceGuid, CancellationToken cancellationToken);

    /// <summary>
    /// Generates the payment information for an instance as HTML.
    /// </summary>
    /// <param name="instanceGuid">The instance to generate content for.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<Stream> GetPaymentAsHtml(Guid instanceGuid, CancellationToken cancellationToken);

    /// <summary>
    /// Generates the form data for an instance as a watermarked PDF.
    /// </summary>
    /// <param name="app">The app the instance belongs to, without the org prefix.</param>
    /// <param name="instanceGuid">The instance holding the data element.</param>
    /// <param name="dataGuid">The data element to generate content for.</param>
    /// <param name="language">The language to generate the content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<Stream> GetFormdataAsPdf(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Generates the form data for an instance as HTML.
    /// </summary>
    /// <param name="app">The app the instance belongs to, without the org prefix.</param>
    /// <param name="instanceGuid">The instance holding the data element.</param>
    /// <param name="dataGuid">The data element to generate content for.</param>
    /// <param name="language">The language to generate the content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="singlePageNr">A single page to generate, or -1 for every visible page.</param>
    Task<Stream> GetFormdataAsHtml(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken,
        int singlePageNr = -1
    );

    /// <summary>
    /// Generates the form summary for an instance as HTML.
    /// </summary>
    /// <param name="app">The app the instance belongs to, without the org prefix.</param>
    /// <param name="instanceGuid">The instance holding the data element.</param>
    /// <param name="dataGuid">The data element to generate content for.</param>
    /// <param name="language">The language to generate the content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<Stream> GetFormSummaryAsHtml(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    );
}
