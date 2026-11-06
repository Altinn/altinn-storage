#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.UnitTest;

internal static class InstanceMutationTestFactory
{
    internal static InstanceInternal ApplyInstanceChanges(
        InstanceInternal snapshot,
        InstanceMutationCommit mutation
    )
    {
        InstanceInternal result = JsonSerializer.Deserialize<InstanceInternal>(
            JsonSerializer.Serialize(snapshot)
        );
        result.InternalId = snapshot.InternalId;
        result.Versions = snapshot.Versions;
        result.Data = snapshot.Data is null ? null : [.. snapshot.Data];
        InstanceMutationChanges changes = mutation.InstanceChanges;
        if (changes is null)
        {
            return result;
        }

        result.Process = changes.Process ?? result.Process;
        if (changes.Status is { } status)
        {
            result.Status ??= new InstanceStatus();
            result.Status.IsArchived = status.IsArchived ?? result.Status.IsArchived;
            result.Status.IsSoftDeleted = status.IsSoftDeleted ?? result.Status.IsSoftDeleted;
            result.Status.IsHardDeleted = status.IsHardDeleted ?? result.Status.IsHardDeleted;
            if (status.Archived.IsSpecified)
            {
                result.Status.Archived = status.Archived.Value;
            }

            if (status.SoftDeleted.IsSpecified)
            {
                result.Status.SoftDeleted = status.SoftDeleted.Value;
            }

            if (status.HardDeleted.IsSpecified)
            {
                result.Status.HardDeleted = status.HardDeleted.Value;
            }

            result.Status.ReadStatus = status.ReadStatus ?? result.Status.ReadStatus;
            result.Status.Substatus = status.Substatus ?? result.Status.Substatus;
        }

        result.DataValues = MergeDictionary(result.DataValues, changes.DataValues);
        result.PresentationTexts = MergeDictionary(
            result.PresentationTexts,
            changes.PresentationTexts
        );
        if (changes.CompleteConfirmations is not null)
        {
            result.CompleteConfirmations =
            [
                .. result.CompleteConfirmations ?? [],
                .. changes.CompleteConfirmations.Where(confirmation =>
                    !result.CompleteConfirmations?.Any(existing =>
                        existing.StakeholderId == confirmation.StakeholderId
                    )
                    ?? true
                ),
            ];
        }

        return result;
    }

    internal static InstanceInternal ApplyInstanceChangesAndStamp(
        InstanceInternal snapshot,
        InstanceMutationCommit mutation
    )
    {
        InstanceInternal result = ApplyInstanceChanges(snapshot, mutation);
        result.LastChanged = mutation.Stamp.LastChanged;
        result.LastChangedBy = mutation.Stamp.LastChangedBy;
        return result;
    }

    private static Dictionary<string, string> MergeDictionary(
        Dictionary<string, string> current,
        IReadOnlyDictionary<string, string> changes
    )
    {
        if (changes is null)
        {
            return current;
        }

        Dictionary<string, string> result = current is null ? [] : new(current);
        foreach ((string key, string value) in changes)
        {
            if (value is null)
            {
                result.Remove(key);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }
}
