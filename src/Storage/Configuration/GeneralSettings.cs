using System;
using System.Collections.Generic;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Configuration object used to hold general settings for the storage application.
/// </summary>
public class GeneralSettings
{
    /// <summary>
    /// Open Id Connect Well known endpoint. Related to JSON WEB token validation.
    /// </summary>
    public string OpenIdWellKnownEndpoint { get; set; }

    /// <summary>
    /// Hostname
    /// </summary>
    public string Hostname { get; set; }

    /// <summary>
    /// Name of the cookie for runtime
    /// </summary>
    public string RuntimeCookieName { get; set; }

    /// <summary>
    /// Gets or sets the URI for the SBL Bridge Authorization API.
    /// </summary>
    public Uri BridgeApiAuthorizationEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the URI for the SBL Bridge Authorization API.
    /// </summary>
    public Uri BridgeApiCorrespondenceEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the scopes for Instance Read.
    /// </summary>
    public List<string> InstanceReadScope { get; set; }

    /// <summary>
    /// Gets or sets the scope for storage sync adapters.
    /// </summary>
    public string InstanceSyncAdapterScope { get; set; }

    /// <summary>
    /// Gets or sets the cache lifetime for text resources.
    /// </summary>
    public int TextResourceCacheLifeTimeInSeconds { get; set; }

    /// <summary>
    /// Gets or sets the cache lifetime for application title dictionary.
    /// </summary>
    public int AppTitleCacheLifeTimeInSeconds { get; set; }

    /// <summary>
    /// Gets or sets the cache lifetime for application metadata document.
    /// </summary>
    public int AppMetadataCacheLifeTimeInSeconds { get; set; }

    /// <summary>
    /// Gets or sets a semicolon separated white list of IP addresses for the migration controller
    /// </summary>
    public string MigrationIpWhiteList { get; set; }

    /// <summary>
    /// Gets or sets the URI for the ondemand API
    /// </summary>
    public string OndemandEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the URI for the pdf generator
    /// </summary>
    public string PdfGeneratorEndpoint { get; set; }

    /// <summary>
    /// Gets or sets whether to use Ttd as service owner for environements without "normal" service owners
    /// </summary>
    public bool A2UseTtdAsServiceOwner { get; set; }

    /// <summary>
    /// Gets or sets whether to disable telemetry for migration. Is true by default. Is intended to
    /// be set to false on demand by manipulating the aks yaml config in the target environment.
    /// </summary>
    public bool DisableTelemetryForMigration { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use write as an action when authorizing A2 search requests.
    /// </summary>
    public bool AuthorizeA2ListInstancesWrite { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use delete as an action when authorizing A2 search requests.
    /// </summary>
    public bool AuthorizeA2ListInstancesDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use sign as an action when authorizing A2 search requests.
    /// </summary>
    public bool AuthorizeA2ListInstancesSign { get; set; } = true;

    /// <summary>
    /// Gets or sets which orgs can access instances through Altinn Studio.
    /// </summary>
    public List<string> StudioInstancesOrgWhiteList { get; set; } = new();
}
