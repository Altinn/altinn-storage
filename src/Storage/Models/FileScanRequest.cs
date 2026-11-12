#nullable disable

using System;

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
}
