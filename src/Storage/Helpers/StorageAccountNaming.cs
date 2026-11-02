using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Derives the storage account of a service owner from the configured naming pattern, used for
/// blobs that have no storage account row.
/// </summary>
public static class StorageAccountNaming
{
    /// <summary>
    /// The storage account number used when an application does not set one.
    /// </summary>
    public const int DefaultStorageAccountNumber = 1;

    private const string _azuriteAccountName = "devstoreaccount1";

    /// <summary>
    /// Gets the storage account number of an application.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application's storage account number, or the default number.</returns>
    public static int NumberOf(Application app)
    {
        return app.StorageAccountNumber ?? DefaultStorageAccountNumber;
    }

    /// <summary>
    /// Gets the Azure blob container that the naming pattern gives for an org and storage account number.
    /// </summary>
    /// <param name="config">The storage configuration holding the naming pattern.</param>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <returns>The provider info of the container.</returns>
    /// <remarks>
    /// The account name is <see cref="AzureStorageConfiguration.OrgStorageAccount"/> formatted with the org,
    /// with its last two characters replaced by the number when it is not the default number. Azurite has
    /// a single account, so the number does not apply there.
    /// </remarks>
    public static AzureBlobProviderInfo PatternDefault(
        AzureStorageConfiguration config,
        string org,
        int number
    )
    {
        string container = string.Format(config.OrgStorageContainer, org);
        if (config.AccountName == _azuriteAccountName)
        {
            return new AzureBlobProviderInfo(
                config.AccountName,
                container,
                new Uri(config.BlobEndPoint)
            );
        }

        string accountName = string.Format(config.OrgStorageAccount, org);
        if (number != DefaultStorageAccountNumber)
        {
            accountName = $"{accountName.AsSpan(0, accountName.Length - 2)}{number:D2}";
        }

        return new AzureBlobProviderInfo(accountName, container);
    }
}
