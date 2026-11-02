#nullable disable

using System;
using System.Text.Json;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// This class represents a request to perform a file scan. Instances is sent to a queue
/// handled by the FileScan system.
/// </summary>
public class FileScanRequest
{
    /// <summary>
    /// Gets or sets the unique id of the data element.
    /// </summary>
    public string DataElementId { get; set; }

    /// <summary>
    /// Gets or sets the unique id of the parent instance of the data element.
    /// </summary>
    /// <remarks>
    /// The instance id contains both the instance owner party id and the unique instance guid.
    /// </remarks>
    public string InstanceId { get; set; }

    /// <summary>
    /// Gets or sets the name of the data element (file)
    /// </summary>
    public string Filename { get; set; }

    /// <summary>
    /// Gets or sets the time when blob was saved.
    /// </summary>
    /// <remarks>
    /// Obsolete: only file scanners from before Altinn/altinn-file-scan#466 read this; newer ones get the blob from
    /// Storage by <see cref="BlobVersionId"/>. Keep it until #466 runs in every environment, so that Storage can be
    /// released before the scanner.
    /// </remarks>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the path to blob storage. Might be nullified in export.
    /// </summary>
    /// <remarks>
    /// Obsolete: only file scanners from before Altinn/altinn-file-scan#466 read this; newer ones get the blob from
    /// Storage by <see cref="BlobVersionId"/>. Keep it until #466 runs in every environment, so that Storage can be
    /// released before the scanner.
    /// </remarks>
    public string BlobStoragePath { get; set; }

    /// <summary>
    /// Gets or sets the explicit blob version ID to scan.
    /// </summary>
    public string BlobVersionId { get; set; }

    /// <summary>
    /// Gets or sets the application owner identifier
    /// </summary>
    /// <remarks>
    /// Obsolete: only file scanners from before Altinn/altinn-file-scan#466 read this; newer ones get the blob from
    /// Storage by <see cref="BlobVersionId"/>. Keep it until #466 runs in every environment, so that Storage can be
    /// released before the scanner.
    /// </remarks>
    public string Org { get; set; }

    /// <summary>
    /// Gets or sets an optional account number for when the application owner have more than one storage account
    /// </summary>
    /// <remarks>
    /// Obsolete: only file scanners from before Altinn/altinn-file-scan#466 read this; newer ones get the blob from
    /// Storage by <see cref="BlobVersionId"/>. Keep it until #466 runs in every environment, so that Storage can be
    /// released before the scanner.
    /// </remarks>
    public int? StorageAccountNumber { get; set; }

    /// <summary>
    /// Gets or sets the provider of the storage account holding the blob, such as
    /// <see cref="Models.StorageAccountProviderInfo.AzureBlob"/>. It selects how
    /// <see cref="StorageAccountProviderInfo"/> is read.
    /// </summary>
    public string StorageAccountProvider { get; set; }

    /// <summary>
    /// Gets or sets where the storage account holding the blob keeps its blobs, as the provider's own
    /// provider info document.
    /// </summary>
    public JsonElement? StorageAccountProviderInfo { get; set; }

    /// <summary>
    /// Gets or sets the id of the storage account row holding the blob, or null when the blob is in
    /// the storage account derived from the naming pattern.
    /// </summary>
    public short? StorageAccountId { get; set; }
}
