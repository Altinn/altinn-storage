#nullable disable

using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Where the blobs of an Azure blob storage account are and how they are accessed.
/// </summary>
/// <param name="ServiceUri">The blob service URI of the account.</param>
/// <param name="ContainerUri">The URI of the container.</param>
/// <param name="UsesSharedKey">
/// Whether the account is accessed with the configured account name and key instead of an Azure identity.
/// </param>
internal sealed record AzureBlobLocation(Uri ServiceUri, Uri ContainerUri, bool UsesSharedKey)
{
    /// <summary>
    /// The account name of Azurite, the local Azure Storage emulator.
    /// </summary>
    internal const string AzuriteAccountName = "devstoreaccount1";

    /// <summary>
    /// Gets the location of a container. The Azurite account is at the configured blob endpoint and is
    /// accessed with the configured shared key; every other account is at its public Azure endpoint and is
    /// accessed with an Azure identity.
    /// </summary>
    /// <param name="info">The container.</param>
    /// <param name="configuration">The storage configuration.</param>
    /// <returns>The location of the container.</returns>
    /// <exception cref="InvalidOperationException">
    /// The container is in the Azurite account but the configuration is not for Azurite.
    /// </exception>
    internal static AzureBlobLocation Of(
        AzureBlobProviderInfo info,
        AzureStorageConfiguration configuration
    )
    {
        if (info.AccountName == AzuriteAccountName)
        {
            if (configuration.AccountName != AzuriteAccountName)
            {
                throw new InvalidOperationException(
                    $"Storage account '{AzuriteAccountName}' is only available when Azurite is configured."
                );
            }

            Uri serviceUri = new(configuration.BlobEndPoint.TrimEnd('/'));
            return new AzureBlobLocation(serviceUri, ContainerIn(serviceUri, info), true);
        }

        Uri publicServiceUri = new($"https://{info.AccountName}.blob.core.windows.net");
        return new AzureBlobLocation(publicServiceUri, ContainerIn(publicServiceUri, info), false);
    }

    private static Uri ContainerIn(Uri serviceUri, AzureBlobProviderInfo info) =>
        new($"{serviceUri.AbsoluteUri.TrimEnd('/')}/{info.Container}");
}
