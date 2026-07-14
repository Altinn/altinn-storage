#nullable enable

using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Result from creating a sign document.
/// </summary>
public sealed record SignDocumentCreateResult
{
    private SignDocumentCreateResult(
        bool created,
        ServiceError? serviceError,
        StorageVersions? versions
    )
    {
        Created = created;
        ServiceError = serviceError;
        Versions = versions;
    }

    /// <summary>
    /// Whether a signing document was created.
    /// </summary>
    public bool Created { get; }

    /// <summary>
    /// Service error when creation failed.
    /// </summary>
    public ServiceError? ServiceError { get; }

    /// <summary>
    /// Current storage-owned versions when they are known.
    /// </summary>
    public StorageVersions? Versions { get; }

    /// <summary>
    /// Creates a successful signing result.
    /// </summary>
    public static SignDocumentCreateResult Success(StorageVersions versions) =>
        new(true, null, versions);

    /// <summary>
    /// Creates an ordinary signing failure result.
    /// </summary>
    public static SignDocumentCreateResult Failure(
        ServiceError serviceError,
        StorageVersions? versions = null
    ) => new(false, serviceError, versions);
}
