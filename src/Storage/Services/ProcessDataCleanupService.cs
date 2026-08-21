using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <inheritdoc cref="IProcessDataCleanupService"/>
/// <summary>
/// Finds stale task-generated data elements for process-transition cleanup.
/// </summary>
public class ProcessDataCleanupService(ILogger<ProcessDataCleanupService> logger)
    : IProcessDataCleanupService
{
    /// <inheritdoc/>
    public IReadOnlyList<DataElementInternal> GetGeneratedFromTaskDataElements(
        InstanceInternal instanceInternal,
        string taskId
    )
    {
        if (instanceInternal.Data is null or { Count: 0 })
        {
            return [];
        }

        List<DataElementInternal> stale = instanceInternal
            .Data.Where(de =>
                de.References?.Any(r =>
                    r.Relation == RelationType.GeneratedFrom
                    && r.ValueType == ReferenceType.Task
                    && r.Value == taskId
                )
                    is true
            )
            .ToList();

        if (stale.Count == 0)
        {
            return [];
        }

        logger.LogInformation(
            "Found {Count} stale data element(s) to clean up for task {TaskId} on instance {InstanceId}",
            stale.Count,
            taskId,
            instanceInternal.Id
        );

        return stale;
    }
}
