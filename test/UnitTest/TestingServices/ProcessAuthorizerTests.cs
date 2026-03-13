#nullable enable
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Interface.Models;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class ProcessAuthorizerTests
{
    private readonly Mock<IAuthorization> _authorizationMock = new();

    private ProcessAuthorizer CreateSut() => new(_authorizationMock.Object);

    private static Instance CreateInstance(
        string taskId = "Task_1",
        string altinnTaskType = "data"
    ) =>
        new()
        {
            Id = "500/guid",
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = taskId,
                    AltinnTaskType = altinnTaskType,
                },
            },
        };

    private void SetupAuthorizeAction(string action, string taskId, bool returns) =>
        _authorizationMock
            .Setup(a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), action, taskId))
            .ReturnsAsync(returns);

    #region AuthorizeProcessNext

    [Theory]
    [InlineData("data", "write")]
    [InlineData("signing", "sign")]
    [InlineData("signing", "write")]
    [InlineData("confirmation", "confirm")]
    [InlineData("payment", "pay")]
    [InlineData("payment", "write")]
    public async Task AuthorizeProcessNext_UserHasAllowedAction_ReturnsTrue(
        string taskType,
        string authorizedAction
    )
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupAuthorizeAction(authorizedAction, "Task_1", true);

        var result = await CreateSut().AuthorizeProcessNext(instance, new ProcessState());

        Assert.True(result);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("signing")]
    [InlineData("confirmation")]
    [InlineData("payment")]
    public async Task AuthorizeProcessNext_UserLacksAllActions_ReturnsFalse(string taskType)
    {
        var instance = CreateInstance(altinnTaskType: taskType);

        var result = await CreateSut().AuthorizeProcessNext(instance, new ProcessState());

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_NoCurrentTask_ReturnsFalse()
    {
        var instance = new Instance { Process = new ProcessState { CurrentTask = null } };

        Assert.False(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
    }

    [Fact]
    public async Task AuthorizeProcessNext_NullProcess_ReturnsFalse()
    {
        var instance = new Instance { Process = null };

        Assert.False(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
    }

    [Fact]
    public async Task AuthorizeProcessNext_AbandonFlow_OnlyChecksReject()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        var nextState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { FlowType = "AbandonCurrentMoveToNext" },
        };
        SetupAuthorizeAction("reject", "Task_1", true);

        Assert.True(await CreateSut().AuthorizeProcessNext(instance, nextState));
        _authorizationMock.Verify(
            a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), "write", It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task AuthorizeProcessNext_NonStandardFlowType_UsesNextProcessStateTaskType()
    {
        var instance = CreateInstance(taskId: "Task_1", altinnTaskType: "data");
        var nextState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_2",
                AltinnTaskType = "signing",
                FlowType = "SomeGatewayFlow",
            },
        };
        SetupAuthorizeAction("sign", "Task_2", true);

        Assert.True(await CreateSut().AuthorizeProcessNext(instance, nextState));
    }

    [Fact]
    public async Task AuthorizeProcessNext_CompleteCurrentMoveToNext_UsesCurrentInstanceTask()
    {
        var instance = CreateInstance(taskId: "Task_1", altinnTaskType: "signing");
        var nextState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_2",
                AltinnTaskType = "data",
                FlowType = "CompleteCurrentMoveToNext",
            },
        };
        SetupAuthorizeAction("sign", "Task_1", true);

        Assert.True(await CreateSut().AuthorizeProcessNext(instance, nextState));
    }

    #endregion

    #region AuthorizeLock

    [Theory]
    [InlineData("data", "write")]
    [InlineData("data", "reject")]
    [InlineData("signing", "sign")]
    [InlineData("signing", "reject")]
    [InlineData("confirmation", "confirm")]
    [InlineData("confirmation", "reject")]
    public async Task AuthorizeLock_UserHasAllowedAction_ReturnsTrue(
        string taskType,
        string authorizedAction
    )
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupAuthorizeAction(authorizedAction, "Task_1", true);

        Assert.True(await CreateSut().AuthorizeDataElementLock(instance));
        Assert.True(await CreateSut().AuthorizeInstanceLock(instance));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("signing")]
    [InlineData("confirmation")]
    public async Task AuthorizeLock_UserLacksAllActions_ReturnsFalse(string taskType)
    {
        var instance = CreateInstance(altinnTaskType: taskType);

        Assert.False(await CreateSut().AuthorizeDataElementLock(instance));
        Assert.False(await CreateSut().AuthorizeInstanceLock(instance));
    }

    [Fact]
    public async Task AuthorizeLock_NoCurrentTask_ReturnsFalse()
    {
        var instance = new Instance { Process = new ProcessState { CurrentTask = null } };

        Assert.False(await CreateSut().AuthorizeDataElementLock(instance));
        Assert.False(await CreateSut().AuthorizeInstanceLock(instance));
    }

    #endregion
}
