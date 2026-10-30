using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

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
    /// Returns why data of this type may not be written in the instance's current task, or null when it may.
    /// </summary>
    /// <remarks>
    /// Neither the caller's identity nor a process transition plays any part: the workflow engine stores the task it
    /// moves to before that task starts, so every write it makes is checked against the task the write belongs to.
    /// The one write made without a current task is the deletion <see cref="ApplicationLogic.AutoDeleteOnProcessEnd"/>
    /// asks for, which is allowed once the process has ended.
    /// </remarks>
    public static string? GetTaskBindingConflict(
        this DataType dataType,
        InstanceInternal instance,
        bool isDelete
    )
    {
        if (dataType.TaskIds is not { Count: > 0 } taskIds)
        {
            return null;
        }

        string? currentTask = instance.Process?.CurrentTask?.ElementId;
        if (currentTask is not null && taskIds.Contains(currentTask))
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

        string boundTasks = string.Join(", ", taskIds);
        return currentTask is null
            ? $"Data element of type {dataType.Id} can only be modified in {boundTasks} (no current task)"
            : $"Data element of type {dataType.Id} can only be modified in {boundTasks} (current task {currentTask})";
    }
}
