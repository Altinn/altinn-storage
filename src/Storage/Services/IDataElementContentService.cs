#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Resolves a data element for read operations and opens its content. All endpoints that serve
/// data element content use this service. Thus they apply the same authorization rules, and they
/// make the same choice between a stored blob and generated on-demand content.
/// </summary>
public interface IDataElementContentService
{
    /// <summary>
    /// Finds the instance, the data element and the application for a data element reference.
    /// Authorizes the caller for read access to the instance. Also authorizes the caller for the
    /// data type's <see cref="Interface.Models.DataType.ActionRequiredToRead"/>.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance the data element belongs to.</param>
    /// <param name="dataGuid">The id of the data element.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <remarks>
    /// This method resolves hard-deleted elements in the same way as all other elements. The
    /// caller decides if it can serve them, because the rule is different for each endpoint.
    /// </remarks>
    Task<(DataElementReadContext Context, ServiceError ServiceError)> ResolveForRead(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Opens the content of a resolved data element. The method reads the content from blob
    /// storage. For migrated Altinn 2 elements, the method generates the content.
    /// </summary>
    /// <param name="context">A data element that <see cref="ResolveForRead"/> resolved.</param>
    /// <param name="language">The language to generate on-demand content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The content, or <c>null</c> when it could not be read or generated.</returns>
    Task<Stream> OpenContent(
        DataElementReadContext context,
        string language,
        CancellationToken cancellationToken
    );
}
