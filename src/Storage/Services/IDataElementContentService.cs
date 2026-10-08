#nullable disable

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Gets the content of data elements, from blob storage or as generated on-demand content.
/// </summary>
public interface IDataElementContentService
{
    /// <summary>
    /// Gets the content of a data element. If the blob storage path identifies on-demand content,
    /// the method generates the content. Otherwise, the method reads the blob.
    /// </summary>
    /// <param name="instance">The instance the data element belongs to.</param>
    /// <param name="dataElement">The data element to get the content of.</param>
    /// <param name="application">The application.</param>
    /// <param name="language">The language for on-demand content. Blob reads ignore it.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>
    /// The content, or <c>null</c> if the blob does not exist or the on-demand content cannot be generated.
    /// </returns>
    Task<Stream> GetContent(
        InstanceInternal instance,
        DataElementInternal dataElement,
        Application application,
        string language,
        CancellationToken cancellationToken
    );
}
