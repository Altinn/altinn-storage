#nullable disable

using System;
using System.Diagnostics;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Shared process-status rules for Storage write admission.
/// </summary>
internal static class ProcessStatusHelper
{
    /// <summary>
    /// Ensures that a caller's expected process status matches the loaded instance.
    /// </summary>
    /// <param name="instance">The instance used to authorize the write.</param>
    /// <param name="expectedProcessStatus">
    /// The caller's expected status. An absent value means <see cref="ProcessStatus.Idle"/>.
    /// </param>
    /// <exception cref="ProcessStatusConflictException">
    /// Thrown when the expected and current statuses differ.
    /// </exception>
    public static void EnsureExpectedStatus(
        InstanceInternal instance,
        ProcessStatus? expectedProcessStatus = null
    )
    {
        ProcessStatus currentProcessStatus = instance.Process?.Status ?? ProcessStatus.Idle;
        expectedProcessStatus ??= ProcessStatus.Idle;

        if (currentProcessStatus != expectedProcessStatus)
        {
            throw new ProcessStatusConflictException(currentProcessStatus);
        }
    }

    /// <summary>
    /// Parses a process status as it is persisted in the instance JSONB and reported back by the SQL
    /// layer. A status Storage cannot represent is refused; anything else Enum.TryParse maps to a
    /// declared status is taken, including casing and numeric forms the write paths cannot persist.
    /// </summary>
    /// <param name="processStatus">The persisted status value.</param>
    public static ProcessStatus ParsePersistedStatus(string processStatus) =>
        Enum.TryParse(processStatus, ignoreCase: true, out ProcessStatus status)
        && Enum.IsDefined(status)
            ? status
            : throw new UnreachableException(
                $"Persisted process status '{processStatus}' is not a known process status."
            );
}
