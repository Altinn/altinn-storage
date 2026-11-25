namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The result when Storage sets the file scan result of a data element.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="WriteResult">
/// The updated data element and instance versions. Set only for <see cref="FileScanStatusOutcome.Updated"/>.
/// </param>
public sealed record FileScanStatusUpdateResult(
    FileScanStatusOutcome Outcome,
    DataElementWriteResult? WriteResult = null
);
