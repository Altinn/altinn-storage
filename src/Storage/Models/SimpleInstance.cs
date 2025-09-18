#nullable enable

using System;
using System.Linq;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Represents a simplified instance with most fields redacted, see <see cref="Instance"/>.
/// </summary>
public class SimpleInstance
{
    /// <summary>
    /// The unique id of the instance {instanceGuid}.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Application owner identifier, usually a abbreviation of organisation name. All in lower case.
    /// </summary>
    [JsonPropertyName("org")]
    public required string Org { get; set; }

    /// <summary>
    /// The id of the application this is an instance of.
    /// </summary>
    [JsonPropertyName("app")]
    public required string App { get; set; }

    /// <summary>
    /// The name of the process element, <see cref="ProcessElementInfo"/>.
    /// </summary>
    [JsonPropertyName("currentTaskName")]
    public string? CurrentTaskName { get; set; }

    /// <summary>
    /// If the instance is read.
    /// </summary>
    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; }

    /// <summary>
    /// Reference to the current task/event element id as given in the process definition, <see cref="ProcessElementInfo"/>.
    /// </summary>
    [JsonPropertyName("currentTaskId")]
    public string? CurrentTaskId { get; set; }

    /// <summary>
    /// The date the instance was archived.
    /// </summary>
    [JsonPropertyName("archivedAt")]
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// The date the instance was deleted.
    /// </summary>
    [JsonPropertyName("softDeletedAt")]
    public DateTimeOffset? SoftDeletedAt { get; set; }

    /// <summary>
    /// The date the instance was marked for hard delete by user.
    /// </summary>
    [JsonPropertyName("hardDeletedAt")]
    public DateTimeOffset? HardDeletedAt { get; set; }

    /// <summary>
    /// The date the instance was marked as confirmed by service owner.
    /// </summary>
    [JsonPropertyName("confirmedAt")]
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>
    /// The date and time for when the instance was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// The date and time for when the instance was last edited.
    /// </summary>
    [JsonPropertyName("lastChangedAt")]
    public DateTimeOffset? LastChangedAt { get; set; }

    /// <summary>
    /// Converts an <see cref="Instance"/> into a <see cref="SimpleInstance"/>.
    /// </summary>
    public static SimpleInstance FromInstance(Instance instance)
    {
        var partyIdPrefix = $"{instance.InstanceOwner.PartyId}/";
        var orgPrefix = $"{instance.Org}/";

        if (!instance.Id.StartsWith(partyIdPrefix))
        {
            throw new InvalidOperationException(
                $"Instance id {instance.Id} has an unexpected format, expected '{{instanceOwnerPartyId}}/{{instanceId}}'.");
        }

        if (!instance.AppId.StartsWith(orgPrefix))
        {
            throw new InvalidOperationException(
                $"App id {instance.AppId} has an unexpected format, expected '{{org}}/{{app}}'.");
        }

        return new SimpleInstance()
        {
            Id = instance.Id.Substring(partyIdPrefix.Length),
            Org = instance.Org,
            App = instance.AppId.Substring(orgPrefix.Length),
            CurrentTaskName = instance.Process?.CurrentTask?.Name,
            CurrentTaskId = instance.Process?.CurrentTask?.ElementId,
            IsRead = instance.Status?.ReadStatus != ReadStatus.Unread,
            ArchivedAt = instance.Status?.Archived,
            SoftDeletedAt = instance.Status?.SoftDeleted,
            HardDeletedAt = instance.Status?.HardDeleted,
            ConfirmedAt = instance
                .CompleteConfirmations?.OrderBy(c => c.ConfirmedOn)
                .FirstOrDefault()
                ?.ConfirmedOn,
            CreatedAt = instance.Created,
            LastChangedAt = instance.LastChanged,
        };
    }
}
