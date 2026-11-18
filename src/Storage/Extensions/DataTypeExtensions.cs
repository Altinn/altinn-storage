using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Utility methods for DataType objects.
/// </summary>
internal static class DataTypeExtensions
{
    /// <summary>
    /// Checks if the user has permission to read data of this type for the given storage instance.
    /// </summary>
    public static async Task<bool> CanRead(
        this DataType dataType,
        IAuthorization authorizationService,
        InstanceInternal instance,
        string? task = null
    )
    {
        if (string.IsNullOrWhiteSpace(dataType.ActionRequiredToRead))
        {
            return true;
        }

        return await authorizationService.AuthorizeInstanceAction(
            instance,
            dataType.ActionRequiredToRead,
            task ?? instance.Process?.CurrentTask?.ElementId
        );
    }

    /// <summary>
    /// Checks if the user has permission to write data of this type for the given storage instance.
    /// </summary>
    public static async Task<bool> CanWrite(
        this DataType dataType,
        IAuthorization authorizationService,
        InstanceInternal instance,
        string? task = null
    )
    {
        if (string.IsNullOrWhiteSpace(dataType.ActionRequiredToWrite))
        {
            return true;
        }

        return await authorizationService.AuthorizeInstanceAction(
            instance,
            dataType.ActionRequiredToWrite,
            task ?? instance.Process?.CurrentTask?.ElementId
        );
    }

    /// <summary>
    /// Whether <see cref="GetTaskBindingConflict"/> restricts data of this type to the tasks in
    /// <see cref="DataType.TaskIds"/>. Storage ignores the lock on elements of such a data type.
    /// </summary>
    public static bool IsTaskBound(this DataType dataType) => dataType.TaskIds is { Count: > 0 };

    /// <summary>
    /// Returns the conflict response explaining why data of this type may not be written in the instance's current task,
    /// or null when it may.
    /// </summary>
    /// <remarks>
    /// The deletion <see cref="ApplicationLogic.AutoDeleteOnProcessEnd"/> asks for is exempt once the process has ended,
    /// because the app makes it after it has stored the ended process, when the instance has no current task.
    /// </remarks>
    public static JsonResult? GetTaskBindingConflict(
        this DataType dataType,
        InstanceInternal instance,
        bool isDelete
    )
    {
        if (!dataType.IsTaskBound())
        {
            return null;
        }

        string? currentTask = instance.Process?.CurrentTask?.ElementId;
        if (currentTask is not null && dataType.TaskIds.Contains(currentTask))
        {
            return null;
        }

        if (
            isDelete
            && dataType.AppLogic?.AutoDeleteOnProcessEnd == true
            && instance.Process?.Ended is not null
        )
        {
            return null;
        }

        string boundTasks = string.Join(", ", dataType.TaskIds);
        return new JsonResult(
            new ProblemDetails
            {
                Type = "task_binding_conflict",
                Title = "Task binding conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = currentTask is null
                    ? $"Data element of type {dataType.Id} can only be modified in {boundTasks} (no current task)"
                    : $"Data element of type {dataType.Id} can only be modified in {boundTasks} (current task {currentTask})",
            }
        )
        {
            ContentType = "application/problem+json",
            StatusCode = StatusCodes.Status409Conflict,
        };
    }
}
