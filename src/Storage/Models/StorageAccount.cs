namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A storage account that blobs are read from or written to.
/// </summary>
/// <param name="Id">
/// The id of the row in <c>storage.storageaccounts</c>, or null for the pattern default derived from
/// configuration.
/// </param>
/// <param name="ProviderInfo">Where the account keeps its blobs.</param>
public sealed record StorageAccount(short? Id, StorageAccountProviderInfo ProviderInfo);
