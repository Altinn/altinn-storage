#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Storage.Models;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Instance update arguments computed from an instance and the list of updated properties.
/// PgInstanceRepository binds them as parameters to storage.updateinstance_v4, and
/// PgInstanceMutationRepository writes a subset of them as the instance update item that
/// storage.applyinstancemutation merges itself.
/// </summary>
internal sealed record InstanceUpdateCommandArguments(
    Guid AlternateId,
    string TopLevelSimpleProperties,
    object DataValues,
    object CompleteConfirmations,
    object PresentationTexts,
    object Status,
    object Substatus,
    object Process,
    DateTime LastChanged,
    object TaskId,
    object Confirmed,
    object ExpectedInstanceVersion,
    object ExpectedProcessStateVersion
)
{
    internal static InstanceUpdateCommandArguments Build(
        InstanceInternal instance,
        List<string> updateProperties,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    ) =>
        new(
            instance.Id,
            CustomSerializer.Serialize(instance, updateProperties),
            updateProperties.Contains(nameof(instance.DataValues))
                ? instance.DataValues
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.CompleteConfirmations))
                ? instance.CompleteConfirmations
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.PresentationTexts))
                ? instance.PresentationTexts
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Status))
                ? CustomSerializer.Serialize(instance.Status, updateProperties)
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Status.Substatus))
                ? instance.Status.Substatus
                : DBNull.Value,
            updateProperties.Contains(nameof(instance.Process)) ? instance.Process : DBNull.Value,
            instance.LastChanged ?? DateTime.UtcNow,
            instance.Process?.CurrentTask?.ElementId ?? (object)DBNull.Value,
            instance.CompleteConfirmations != null
            && instance.CompleteConfirmations.Any(c => c.StakeholderId == instance.Org)
                ? true
                : DBNull.Value,
            expectedInstanceVersion ?? (object)DBNull.Value,
            expectedProcessStateVersion ?? (object)DBNull.Value
        );
}
