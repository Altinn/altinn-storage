namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A row in <c>storage.storageaccounts</c> whose provider info this build cannot read, such as a row
/// with a provider this build does not know or a document that is invalid for its provider.
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Provider">The provider stored in the row's provider column.</param>
/// <param name="Org">The blob storage org the account serves.</param>
/// <param name="StorageAccountNumber">The application storage account number the account serves.</param>
/// <param name="Writable">Whether new blob versions for the org and number are written to the account.</param>
/// <param name="Error">Why the provider info could not be read.</param>
public sealed record UnreadableStorageAccountRow(
    short Id,
    string Provider,
    string Org,
    int StorageAccountNumber,
    bool Writable,
    string Error
)
{
    /// <summary>
    /// Gets a description of the row and why it cannot be read, for logs and exceptions.
    /// </summary>
    public string Description =>
        $"Storage account {Id} has provider '{Provider}' with a provider info document this build cannot read: {Error}";
}
