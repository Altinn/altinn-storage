#nullable disable

using System;
using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Explicit mappings between storage instances and the HTTP API model.
/// </summary>
internal static class InstanceExtensions
{
    /// <summary>
    /// Maps a storage instance to the API model. Nested mutable values are intentionally shared.
    /// </summary>
    internal static Instance ToApiModel(this InstanceInternal instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return new Instance
        {
            Id = ToApiId(instance),
            InstanceOwner = instance.InstanceOwner,
            AppId = instance.AppId,
            Org = instance.Org,
            DueBefore = instance.DueBefore,
            VisibleAfter = instance.VisibleAfter,
            Process = instance.Process,
            Status = instance.Status,
            CompleteConfirmations = instance.CompleteConfirmations,
            Data = instance.Data?.Select(dataElement => dataElement.ToApiModel()).ToList(),
            PresentationTexts = instance.PresentationTexts,
            DataValues = instance.DataValues,
            Created = instance.Created,
            CreatedBy = instance.CreatedBy,
            LastChanged = instance.LastChanged,
            LastChangedBy = instance.LastChangedBy,
        };
    }

    /// <summary>
    /// Maps an API instance entering storage to the domain model. Nested mutable values are
    /// intentionally shared, while data elements are independently mapped domain values.
    /// </summary>
    internal static InstanceInternal FromApiModel(this Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return new InstanceInternal
        {
            Id = ParseStorageId(instance),
            InstanceOwner = instance.InstanceOwner,
            AppId = instance.AppId,
            Org = instance.Org,
            DueBefore = instance.DueBefore,
            VisibleAfter = instance.VisibleAfter,
            Process = instance.Process,
            Status = instance.Status,
            CompleteConfirmations = instance.CompleteConfirmations,
            Data = instance.Data?.Select(dataElement => dataElement.FromApiModel()).ToList(),
            PresentationTexts = instance.PresentationTexts,
            DataValues = instance.DataValues,
            Created = instance.Created,
            CreatedBy = instance.CreatedBy,
            LastChanged = instance.LastChanged,
            LastChangedBy = instance.LastChangedBy,
        };
    }

    private static Guid ParseStorageId(Instance instance)
    {
        if (instance.Id is null)
        {
            return Guid.Empty;
        }

        string[] parts = instance.Id.Split('/');
        return Guid.Parse(parts.Length > 1 ? parts[1] : parts[0]);
    }

    private static string ToApiId(InstanceInternal instance)
    {
        string partyId = instance.InstanceOwner?.PartyId;
        return !string.IsNullOrWhiteSpace(partyId) && instance.Id != Guid.Empty
            ? $"{partyId}/{instance.Id}"
            : instance.Id.ToString();
    }
}
