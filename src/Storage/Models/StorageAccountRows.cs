using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Every row in <c>storage.storageaccounts</c>, split by whether this build can read the row's
/// provider info.
/// </summary>
/// <param name="Readable">The rows this build can read, ordered by id.</param>
/// <param name="Unreadable">The rows this build cannot read, ordered by id.</param>
public sealed record StorageAccountRows(
    IReadOnlyList<StorageAccountRow> Readable,
    IReadOnlyList<UnreadableStorageAccountRow> Unreadable
);
