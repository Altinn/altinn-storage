#nullable enable
using System.Collections.Generic;
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

    private static Instance CreateInstance(string taskId = "Task_1", string altinnTaskType = "data")
    {
        return new Instance
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
    }

    private void SetupAuthorizeAction(string action, string taskId, bool returns)
    {
        _authorizationMock
            .Setup(a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), action, taskId))
            .ReturnsAsync(returns);
    }

    #region AuthorizeProcessNext

    [Fact]
    public async Task AuthorizeProcessNext_NoCurrentTask_ReturnsFalse()
    {
        var instance = new Instance { Process = new ProcessState { CurrentTask = null } };
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_NullProcess_ReturnsFalse()
    {
        var instance = new Instance { Process = null };
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_DataTask_UserHasWrite_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_DataTask_UserLacksWrite_ReturnsFalse()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", false);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_SigningTask_UserHasSignButNotWrite_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "signing");
        SetupAuthorizeAction("sign", "Task_1", true);
        SetupAuthorizeAction("write", "Task_1", false);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_SigningTask_UserHasWriteButNotSign_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "signing");
        SetupAuthorizeAction("sign", "Task_1", false);
        SetupAuthorizeAction("write", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_AbandonFlow_AuthorizesRejectAction()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        var nextProcessState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { FlowType = "AbandonCurrentMoveToNext" },
        };
        SetupAuthorizeAction("reject", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance, nextProcessState);

        Assert.True(result);
        _authorizationMock.Verify(
            a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), "write", It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task AuthorizeProcessNext_AbandonFlow_UserLacksReject_ReturnsFalse()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        var nextProcessState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { FlowType = "AbandonCurrentMoveToNext" },
        };
        SetupAuthorizeAction("reject", "Task_1", false);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance, nextProcessState);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeProcessNext_NonStandardFlowType_UsesNextProcessStateTaskType()
    {
        var instance = CreateInstance(taskId: "Task_1", altinnTaskType: "data");
        var nextProcessState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_2",
                AltinnTaskType = "signing",
                FlowType = "SomeGatewayFlow",
            },
        };
        SetupAuthorizeAction("sign", "Task_2", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance, nextProcessState);

        Assert.True(result);
        _authorizationMock.Verify(
            a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), "sign", "Task_2"),
            Times.Once
        );
    }

    [Fact]
    public async Task AuthorizeProcessNext_CompleteCurrentMoveToNext_UsesCurrentInstanceTask()
    {
        var instance = CreateInstance(taskId: "Task_1", altinnTaskType: "signing");
        var nextProcessState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_2",
                AltinnTaskType = "data",
                FlowType = "CompleteCurrentMoveToNext",
            },
        };
        SetupAuthorizeAction("sign", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeProcessNext(instance, nextProcessState);

        Assert.True(result);
        _authorizationMock.Verify(
            a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), "sign", "Task_1"),
            Times.Once
        );
    }

    #endregion

    #region AuthorizeLock

    [Fact]
    public async Task AuthorizeDataElementLock_NoCurrentTask_ReturnsFalse()
    {
        var instance = new Instance { Process = new ProcessState { CurrentTask = null } };
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeInstanceLock_NoCurrentTask_ReturnsFalse()
    {
        var instance = new Instance { Process = new ProcessState { CurrentTask = null } };
        var sut = CreateSut();

        var result = await sut.AuthorizeInstanceLock(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeDataElementLock_DataTask_UserHasWrite_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeDataElementLock_SigningTask_UserHasSignOnly_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "signing");
        SetupAuthorizeAction("sign", "Task_1", true);
        SetupAuthorizeAction("write", "Task_1", false);
        SetupAuthorizeAction("reject", "Task_1", false);
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeDataElementLock_UserHasRejectOnly_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", false);
        SetupAuthorizeAction("reject", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeInstanceLock_UserHasRejectOnly_ReturnsTrue()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", false);
        SetupAuthorizeAction("reject", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeInstanceLock(instance);

        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeDataElementLock_UserHasNoActions_ReturnsFalse()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        SetupAuthorizeAction("write", "Task_1", false);
        SetupAuthorizeAction("reject", "Task_1", false);
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeDataElementLock_RejectIsAlwaysChecked_RegardlessOfTaskType()
    {
        var instance = CreateInstance(altinnTaskType: "confirmation");
        SetupAuthorizeAction("confirm", "Task_1", false);
        SetupAuthorizeAction("reject", "Task_1", true);
        var sut = CreateSut();

        var result = await sut.AuthorizeDataElementLock(instance);

        Assert.True(result);
        _authorizationMock.Verify(
            a => a.AuthorizeInstanceAction(It.IsAny<Instance>(), "reject", "Task_1"),
            Times.Once
        );
    }

    #endregion
}
