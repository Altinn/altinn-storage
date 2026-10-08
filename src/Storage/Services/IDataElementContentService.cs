#nullable disable

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Opens the content of a data element. All endpoints that serve data element content use this
/// service. Thus they make the same choice between a stored blob and generated on-demand content.
/// </summary>
public interface IDataElementContentService
{
    /// <summary>
    /// Opens the content of a data element. The method reads the content from blob storage. For
    /// migrated Altinn 2 elements, the method generates the content.
    /// </summary>
    /// <param name="instance">The instance the data element belongs to.</param>
    /// <param name="dataElement">The data element to open the content of.</param>
    /// <param name="application">The application the instance belongs to.</param>
    /// <param name="language">The language to generate on-demand content in.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The content, or <c>null</c> when it could not be read or generated.</returns>
    /// <remarks>
    /// The method does not do authorization. The caller must make sure that the caller has
    /// permission to read the data element.
    /// </remarks>
    Task<Stream> OpenContent(
        InstanceInternal instance,
        DataElementInternal dataElement,
        Application application,
        string language,
        CancellationToken cancellationToken
    );
}
