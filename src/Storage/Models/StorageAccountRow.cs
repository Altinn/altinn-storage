using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A row in <c>storage.storageaccounts</c>.
/// </summary>
/// <param name="Id">The row id, recorded on blob versions written to the account.</param>
/// <param name="ProviderInfo">Where the account keeps its blobs.</param>
/// <param name="Org">The blob storage org the account serves.</param>
/// <param name="StorageAccountNumber">The application storage account number the account serves.</param>
/// <param name="Writable">Whether new blob versions for the org and number are written to the account.</param>
/// <param name="Created">When the row was created.</param>
public sealed record StorageAccountRow(
    short Id,
    StorageAccountProviderInfo ProviderInfo,
    string Org,
    int StorageAccountNumber,
    bool Writable,
    DateTimeOffset Created
);
