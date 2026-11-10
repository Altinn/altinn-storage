#nullable disable

using System;
using System.Linq;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public class DataTypeExtensionsTests
{
    [Theory]
    [InlineData(null, "Task_1", false)]
    [InlineData(new string[0], "Task_1", false)]
    [InlineData(new string[0], null, true)]
    [InlineData(new[] { "Task_1", "Task_2" }, "Task_1", false)]
    [InlineData(new[] { "Task_1", "Task_2" }, "Task_2", true)]
    public void GetTaskBindingConflict_UnboundOrInItsTasks_ReturnsNull(
        string[] taskIds,
        string currentTask,
        bool isDelete
    )
    {
        DataType dataType = new() { Id = "model", TaskIds = taskIds?.ToList() };

        Assert.Null(dataType.GetTaskBindingConflict(CreateInstance(currentTask), isDelete));
    }

    [Theory]
    [InlineData(
        new[] { "Task_2", "Task_3" },
        "Task_1",
        false,
        "Data element of type model can only be modified in Task_2, Task_3 (current task Task_1)"
    )]
    [InlineData(
        new[] { "Task_2" },
        "Task_1",
        true,
        "Data element of type model can only be modified in Task_2 (current task Task_1)"
    )]
    [InlineData(
        new[] { "Task_2" },
        null,
        false,
        "Data element of type model can only be modified in Task_2 (no current task)"
    )]
    public void GetTaskBindingConflict_OutsideItsTasks_NamesDataTypeTasksAndCurrentTask(
        string[] taskIds,
        string currentTask,
        bool isDelete,
        string expected
    )
    {
        DataType dataType = new() { Id = "model", TaskIds = [.. taskIds] };

        ProblemDetails conflict = dataType.GetTaskBindingConflict(
            CreateInstance(currentTask),
            isDelete
        );

        Assert.Equal("task_binding_conflict", conflict.Type);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.Status);
        Assert.Equal(expected, conflict.Detail);
    }

    [Fact]
    public void GetTaskBindingConflict_DeleteOfAutoDeleteOnProcessEndDataAfterProcessEnd_ReturnsNull()
    {
        DataType dataType = CreateAutoDeleteOnProcessEndDataType();

        Assert.Null(
            dataType.GetTaskBindingConflict(
                CreateInstance(currentTask: null, ended: true),
                isDelete: true
            )
        );
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void GetTaskBindingConflict_AutoDeleteOnProcessEndExemptsOnlyDeletesAfterProcessEnd(
        bool isDelete,
        bool autoDeleteOnProcessEnd,
        bool ended
    )
    {
        DataType dataType = CreateAutoDeleteOnProcessEndDataType();
        dataType.AppLogic.AutoDeleteOnProcessEnd = autoDeleteOnProcessEnd;

        Assert.Equal(
            "Data element of type model can only be modified in Task_2 (no current task)",
            dataType
                .GetTaskBindingConflict(CreateInstance(currentTask: null, ended), isDelete)
                ?.Detail
        );
    }

    [Fact]
    public void GetTaskBindingConflict_IgnoresTaskId()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        DataType dataType = new() { Id = "model", TaskId = "Task_2" };
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Null(dataType.GetTaskBindingConflict(CreateInstance("Task_1"), isDelete: false));
    }

    private static DataType CreateAutoDeleteOnProcessEndDataType() =>
        new()
        {
            Id = "model",
            TaskIds = ["Task_2"],
            AppLogic = new ApplicationLogic { AutoDeleteOnProcessEnd = true },
        };

    private static InstanceInternal CreateInstance(string currentTask, bool ended = false) =>
        new()
        {
            Process = new ProcessState
            {
                CurrentTask = currentTask is null
                    ? null
                    : new ProcessElementInfo { ElementId = currentTask },
                Ended = ended ? DateTime.UtcNow : null,
            },
        };
}
