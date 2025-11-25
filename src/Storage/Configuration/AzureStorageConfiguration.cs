using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Settings for Azure storage
/// </summary>
public class AzureStorageConfiguration
{
    /// <summary>
    /// storage account name
    /// </summary>
    public string AccountName { get; set; }

    /// <summary>
    /// storage account key
    /// </summary>
    public string AccountKey { get; set; }

    /// <summary>
    /// url for the blob end point
    /// </summary>
    public string BlobEndPoint { get; set; }

    /// <summary>
    /// name of app owner storage account
    /// </summary>
    public string OrgStorageAccount { get; set; }

    /// <summary>
    /// name of storage container in app owner storage account
    /// </summary>
    public string OrgStorageContainer { get; set; }
}
