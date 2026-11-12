namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from setting the file scan status of a data element.
/// </summary>
/// <param name="Outcome">Whether the status was set, and if not, why.</param>
/// <param name="WriteResult">The updated data element and instance versions when the status was set.</param>
public sealed record FileScanStatusUpdateResult(
    FileScanStatusOutcome Outcome,
    DataElementWriteResult? WriteResult = null
);
