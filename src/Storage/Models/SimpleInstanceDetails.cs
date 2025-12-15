#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Represents a simplified instance with most fields redacted, see <see cref="Instance"/>.
/// </summary>
public class SimpleInstanceDetails : SimpleInstance
{
    /// <summary>
    /// A list of simplified data elements associated with the instance
    /// </summary>
    [JsonPropertyName("data")]
    public List<SimpleDataElement>? Data { get; set; }

    /// <summary>
    /// Converts an <see cref="Instance"/> into a <see cref="SimpleInstanceDetails"/>.
    /// </summary>
    public static new SimpleInstanceDetails FromInstance(Instance instance)
    {
        var simpleInstance = SimpleInstance.FromInstance(instance);
        var data = instance.Data?.Select(SimpleDataElement.FromDataElement).ToList();

        return new SimpleInstanceDetails()
        {
            Id = simpleInstance.Id,
            Org = simpleInstance.Org,
            App = simpleInstance.App,
            IsRead = simpleInstance.IsRead,
            CurrentTaskId = simpleInstance.CurrentTaskId,
            CurrentTaskName = simpleInstance.CurrentTaskName,
            CompletedAt = simpleInstance.CompletedAt,
            ArchivedAt = simpleInstance.ArchivedAt,
            SoftDeletedAt = simpleInstance.SoftDeletedAt,
            HardDeletedAt = simpleInstance.HardDeletedAt,
            ConfirmedAt = simpleInstance.ConfirmedAt,
            CreatedAt = simpleInstance.CreatedAt,
            LastChangedAt = simpleInstance.LastChangedAt,
            Data = data,
        };
    }
}
