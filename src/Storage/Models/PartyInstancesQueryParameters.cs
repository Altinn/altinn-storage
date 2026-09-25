using System;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The query parameters of the party export of instances.
/// </summary>
public class PartyInstancesQueryParameters
{
    /// <summary>
    /// The maximum number of instances in one batch.
    /// </summary>
    [FromQuery(Name = "size")]
    public int? Size { get; set; }

    /// <summary>
    /// The oldest date to include.
    /// </summary>
    [FromQuery(Name = "dateFrom")]
    public DateTime? DateFrom { get; set; }

    /// <summary>
    /// The newest date to include.
    /// </summary>
    [FromQuery(Name = "dateTo")]
    public DateTime? DateTo { get; set; }

    /// <summary>
    /// The token from the previous batch.
    /// </summary>
    [FromQuery(Name = "continuationToken")]
    public string? ContinuationToken { get; set; }
}
