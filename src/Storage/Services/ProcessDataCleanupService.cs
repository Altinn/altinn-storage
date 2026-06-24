using System;
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
public class ProcessDataCleanupService(
    IDataService _dataService,
    IApplicationService _applicationService,
    ILogger<ProcessDataCleanupService> _logger
) : IProcessDataCleanupService
{
    /// <inheritdoc/>
    public async Task<InstanceInternal> CleanupGeneratedFromTask(
        InstanceInternal instanceInternal,
        string taskId,
        CancellationToken cancellationToken
    )
    {
        if (instanceInternal.DataElements is null or { Count: 0 })
        {
            return instanceInternal;
        }

        var dataElementsInternal = instanceInternal.DataElements.ToList();

        var stale = dataElementsInternal
            .Where(de =>
                de.DataElement.References?.Any(r =>
                    r.Relation == RelationType.GeneratedFrom
                    && r.ValueType == ReferenceType.Task
                    && r.Value == taskId
                )
                    is true
            )
            .ToList();

        if (stale.Count == 0)
        {
            return instanceInternal;
        }

        _logger.LogInformation(
            "Found {Count} stale data element(s) to delete for task {TaskId} on instance {InstanceId}",
            stale.Count,
            taskId,
            instanceInternal.Instance.Id
        );

        int? storageAccountNumber = await GetStorageAccountNumber(instanceInternal);
        int deleted = 0;
        foreach (var dataElementInternal in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _dataService.DeleteImmediately(
                    instanceInternal,
                    dataElementInternal,
                    storageAccountNumber
                );
                dataElementsInternal.Remove(dataElementInternal);
                instanceInternal.Instance.Data.Remove(dataElementInternal.DataElement);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete stale data element {DataElementId} ({BlobStoragePath}) for task {TaskId} on instance {InstanceId}; continuing",
                    dataElementInternal.DataElement.Id,
                    dataElementInternal.DataElement.BlobStoragePath,
                    taskId,
                    instanceInternal.Instance.Id
                );
            }
        }

        _logger.LogInformation(
            "Deleted {Deleted}/{Total} stale data element(s) for task {TaskId} on instance {InstanceId}",
            deleted,
            stale.Count,
            taskId,
            instanceInternal.Instance.Id
        );

        return new(instanceInternal.Instance, dataElementsInternal);
    }

    private async Task<int?> GetStorageAccountNumber(InstanceInternal instanceInternal)
    {
        (Application? application, ServiceError? error) =
            await _applicationService.GetApplicationOrErrorAsync(instanceInternal.Instance.AppId);

        if (application is null)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve application for {instanceInternal.Instance.AppId}: [{error?.ErrorCode}] {error?.ErrorMessage}"
            );
        }

        return application.StorageAccountNumber;
    }
}
