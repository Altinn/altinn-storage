#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Resolves a data element for reading and opens its content, so that every endpoint serving data
/// element content applies the same authorization rules and makes the same choice between a stored
/// blob and generated on-demand content.
/// </summary>
public interface IDataElementContentService
{
    /// <summary>
    /// Resolves the instance, data element and application behind a data element reference and
    /// authorizes the caller to read it, both for the instance and for the data type's
    /// <see cref="Interface.Models.DataType.ActionRequiredToRead"/>.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance the data element belongs to.</param>
    /// <param name="dataGuid">The id of the data element.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <remarks>
    /// Hard-deleted elements resolve like any other. Whether they may be served is left to the
    /// caller, because the rule differs between endpoints.
    /// </remarks>
    Task<(DataElementReadContext Context, ServiceError ServiceError)> ResolveForRead(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Opens the content of a resolved data element, reading it from blob storage or, for migrated
    /// Altinn 2 elements, generating it.
    /// </summary>
    /// <param name="context">A data element resolved by <see cref="ResolveForRead"/>.</param>
    /// <param name="language">The language to generate on-demand content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The content, or <c>null</c> when it could not be read or generated.</returns>
    Task<Stream> OpenContent(
        DataElementReadContext context,
        string language,
        CancellationToken cancellationToken
    );
}
