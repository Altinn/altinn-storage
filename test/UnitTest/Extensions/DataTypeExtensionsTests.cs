#nullable disable

using System.Linq;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public class DataTypeExtensionsTests
{
    [Theory]
    [InlineData(null, "Task_1", false)]
    [InlineData(new string[0], "Task_1", false)]
    [InlineData(new string[0], null, false)]
    [InlineData(new[] { "Task_1", "Task_2" }, "Task_1", false)]
    [InlineData(new[] { "Task_2" }, "Task_1", true)]
    [InlineData(new[] { "Task_2" }, null, true)]
    public void GetTaskBindingConflict_UnboundOrInItsTasksOrInTransition_ReturnsNull(
        string[] taskIds,
        string currentTask,
        bool isPartOfProcessTransition
    )
    {
        DataType dataType = new() { Id = "model", TaskIds = taskIds?.ToList() };

        Assert.Null(
            dataType.GetTaskBindingConflict(CreateInstance(currentTask), isPartOfProcessTransition)
        );
    }

    [Theory]
    [InlineData(
        new[] { "Task_2", "Task_3" },
        "Task_1",
        "Data element of type model can only be modified in Task_2, Task_3 (current task Task_1)"
    )]
    [InlineData(
        new[] { "Task_2" },
        null,
        "Data element of type model can only be modified in Task_2 (no current task)"
    )]
    public void GetTaskBindingConflict_OutsideItsTasks_NamesDataTypeTasksAndCurrentTask(
        string[] taskIds,
        string currentTask,
        string expected
    )
    {
        DataType dataType = new() { Id = "model", TaskIds = [.. taskIds] };

        Assert.Equal(
            expected,
            dataType.GetTaskBindingConflict(
                CreateInstance(currentTask),
                isPartOfProcessTransition: false
            )
        );
    }

    [Fact]
    public void GetTaskBindingConflict_IgnoresTaskId()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        DataType dataType = new() { Id = "model", TaskId = "Task_2" };
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Null(
            dataType.GetTaskBindingConflict(
                CreateInstance("Task_1"),
                isPartOfProcessTransition: false
            )
        );
    }

    private static InstanceInternal CreateInstance(string currentTask) =>
        new()
        {
            Process = new ProcessState
            {
                CurrentTask = currentTask is null
                    ? null
                    : new ProcessElementInfo { ElementId = currentTask },
            },
        };
}
