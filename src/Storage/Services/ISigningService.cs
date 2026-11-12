#nullable disable

using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// This interface describes the required methods and features of a signing service implementation.
/// </summary>
public interface ISigningService
{
    /// <summary>
    /// Create signature document for given data elements, this includes creating md5 hash for all blobs listed.
    /// </summary>
    /// <param name="instance">The instance being signed, or null when it does not exist</param>
    /// <param name="signRequest">Sign request containing data element ids and sign status</param>
    /// <param name="performedBy">User id or org no for the authenticated user</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<SignDocumentCreateResult> CreateSignDocument(
        InstanceInternal instance,
        SignRequest signRequest,
        string performedBy,
        int? expectedInstanceVersion,
        int? expectedProcessStateVersion,
        CancellationToken cancellationToken
    );
}
