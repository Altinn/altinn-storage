using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <inheritdoc cref="IProcessDataCleanupService"/>
/// <summary>
/// Initializes a new instance of the <see cref="ProcessDataCleanupService"/> class.
/// </summary>
public class ProcessDataCleanupService(ILogger<ProcessDataCleanupService> _logger)
    : IProcessDataCleanupService
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<DataElementInternal>> GetGeneratedFromTaskDataElements(
        InstanceInternal instanceInternal,
        string taskId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (instanceInternal.Data is null or { Count: 0 })
        {
            return Task.FromResult<IReadOnlyList<DataElementInternal>>([]);
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
            return Task.FromResult<IReadOnlyList<DataElementInternal>>([]);
        }

        _logger.LogInformation(
            "Found {Count} stale data element(s) to clean up for task {TaskId} on instance {InstanceId}",
            stale.Count,
            taskId,
            instanceInternal.Id
        );

        return Task.FromResult<IReadOnlyList<DataElementInternal>>(stale);
    }
}
