#nullable disable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models;

/// <summary>
/// Response body from an aggregate instance mutation.
/// </summary>
public class InstanceMutationResponse
{
    /// <summary>
    /// Updated instance snapshot after the committed mutation.
    /// </summary>
    [JsonProperty(PropertyName = "instance")]
    public Instance Instance { get; set; }

    /// <summary>
    /// Storage-generated ids for created data elements, in createDataElements request order.
    /// </summary>
    [JsonProperty(PropertyName = "createdDataElementIds")]
    public List<string> CreatedDataElementIds { get; set; }

    /// <summary>
    /// Whether this response was replayed from a previously committed idempotent mutation.
    /// </summary>
    [JsonProperty(PropertyName = "replayed")]
    public bool Replayed { get; set; }

    /// <summary>
    /// The caller result kept with the idempotency record of the mutation: the request's own value when
    /// the mutation was committed, and the value of the committed mutation when it was replayed.
    /// </summary>
    [JsonProperty(PropertyName = "callerResult")]
    public string CallerResult { get; set; }
}
