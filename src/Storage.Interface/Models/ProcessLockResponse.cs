using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// Represents a response when acquiring a process lock.
/// </summary>
public class ProcessLockResponse
{
    /// <summary>
    /// Gets or sets the lock ID.
    /// </summary>
    [JsonProperty(PropertyName = "lockId")]
    [JsonPropertyName("lockId")]
    public Guid LockId { get; set; }
}
