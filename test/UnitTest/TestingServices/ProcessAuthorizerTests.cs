using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using AltinnCore.Authentication.Constants;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class ProcessAuthorizerTests
{
    private const string ServiceOwner = "ttd";

    private readonly Mock<IAuthorization> _authorizationMock = new();
    private readonly Mock<IClaimsPrincipalProvider> _claimsPrincipalProviderMock = new();

    public ProcessAuthorizerTests()
    {
        _claimsPrincipalProviderMock.Setup(p => p.GetUser()).Returns(new ClaimsPrincipal());
    }

    private static readonly IOptions<GeneralSettings> _settings = Options.Create(
        new GeneralSettings { InstanceSyncAdapterScope = "altinn:storage/instances.syncadapter" }
    );

    private ProcessAuthorizer CreateSut() =>
        new(_authorizationMock.Object, _claimsPrincipalProviderMock.Object, _settings);

    private static InstanceInternal CreateInstance(
        string taskId = "Task_1",
        string altinnTaskType = "data"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Org = ServiceOwner,
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = taskId,
                    AltinnTaskType = altinnTaskType,
                },
            },
        };

    private void SetupCallerOrg(string org) =>
        _claimsPrincipalProviderMock
            .Setup(p => p.GetUser())
            .Returns(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(AltinnCoreClaimTypes.Org, org)]))
            );

    private void VerifyPdpNeverAsked() =>
        _authorizationMock.Verify(
            a =>
                a.AuthorizeInstanceAction(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );

    private void SetupAuthorizeAction(string action, string taskId, bool returns) =>
        _authorizationMock
            .Setup(a => a.AuthorizeInstanceAction(It.IsAny<InstanceInternal>(), action, taskId))
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
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };

        Assert.False(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
    }

    [Fact]
    public async Task AuthorizeProcessNext_NullProcess_ReturnsFalse()
    {
        var instance = new InstanceInternal { Process = null };

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
            a =>
                a.AuthorizeInstanceAction(
                    It.IsAny<InstanceInternal>(),
                    "write",
                    It.IsAny<string>()
                ),
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

    [Theory]
    [InlineData("confirmation")]
    [InlineData("customServiceTask")]
    public async Task AuthorizeProcessNext_UserHasWrite_ReturnsFalse(string taskType)
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupAuthorizeAction("write", "Task_1", true);

        Assert.False(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
    }

    #endregion

    #region Service owner

    [Theory]
    [InlineData("data")]
    [InlineData("confirmation")]
    [InlineData("customServiceTask")]
    public async Task AuthorizeProcessNext_ServiceOwner_ReturnsTrueWithoutAskingThePdp(
        string taskType
    )
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupCallerOrg(ServiceOwner);

        Assert.True(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
        VerifyPdpNeverAsked();
    }

    [Fact]
    public async Task AuthorizeProcessNext_ServiceOwnerAbandonFlow_ReturnsTrueWithoutAskingThePdp()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        var nextState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { FlowType = "AbandonCurrentMoveToNext" },
        };
        SetupCallerOrg(ServiceOwner);

        Assert.True(await CreateSut().AuthorizeProcessNext(instance, nextState));
        VerifyPdpNeverAsked();
    }

    [Fact]
    public async Task AuthorizeProcessNext_ServiceOwnerNoCurrentTask_ReturnsFalse()
    {
        var instance = new InstanceInternal
        {
            Org = ServiceOwner,
            Process = new ProcessState { CurrentTask = null },
        };
        SetupCallerOrg(ServiceOwner);

        Assert.False(await CreateSut().AuthorizeProcessNext(instance, new ProcessState()));
    }

    [Theory]
    [InlineData("confirmation")]
    [InlineData("customServiceTask")]
    [InlineData(null)]
    public async Task AuthorizeLockAndUpdate_ServiceOwner_ReturnsTrueWithoutAskingThePdp(
        string? taskType
    )
    {
        var instance = taskType is null
            ? new InstanceInternal
            {
                Org = ServiceOwner,
                Process = new ProcessState { CurrentTask = null },
            }
            : CreateInstance(altinnTaskType: taskType);
        SetupCallerOrg(ServiceOwner);
        var sut = CreateSut();

        Assert.True(await sut.AuthorizeInstanceLock(instance));
        Assert.True(await sut.AuthorizeDataElementLock(instance));
        Assert.True(await sut.AuthorizePresentationTextsUpdate(instance));
        Assert.True(await sut.AuthorizeDataValuesUpdate(instance));
        VerifyPdpNeverAsked();
    }

    [Theory]
    [InlineData("data")]
    [InlineData("confirmation")]
    [InlineData("customServiceTask")]
    public async Task AllChecks_OtherOrgWithoutGrants_ReturnFalse(string taskType)
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupCallerOrg("other-org");
        var sut = CreateSut();

        Assert.False(await sut.AuthorizeProcessNext(instance, new ProcessState()));
        Assert.False(await sut.AuthorizeInstanceLock(instance));
        Assert.False(await sut.AuthorizeDataElementLock(instance));
        Assert.False(await sut.AuthorizePresentationTextsUpdate(instance));
        Assert.False(await sut.AuthorizeDataValuesUpdate(instance));
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
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };

        Assert.False(await CreateSut().AuthorizeDataElementLock(instance));
        Assert.False(await CreateSut().AuthorizeInstanceLock(instance));
    }

    [Fact]
    public async Task AuthorizeLock_NoCurrentTask_UserHasWriteAccess_ReturnsTrue()
    {
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };
        _authorizationMock
            .Setup(a => a.AuthorizeInstanceAction(instance, "write", null))
            .ReturnsAsync(true);

        var sut = CreateSut();

        Assert.True(await sut.AuthorizeInstanceLock(instance));
        Assert.True(await sut.AuthorizeDataElementLock(instance));
    }

    [Fact]
    public async Task AuthorizeLock_NoProcess_ReturnsFalse()
    {
        var instance = new InstanceInternal { Process = null };

        Assert.False(await CreateSut().AuthorizeDataElementLock(instance));
        Assert.False(await CreateSut().AuthorizeInstanceLock(instance));
    }

    #endregion

    #region AuthorizePresentationTextsUpdate and AuthorizeDataValuesUpdate

    [Theory]
    [InlineData("data", "write")]
    [InlineData("data", "reject")]
    [InlineData("signing", "sign")]
    [InlineData("signing", "reject")]
    [InlineData("confirmation", "confirm")]
    [InlineData("confirmation", "reject")]
    public async Task AuthorizeUpdate_UserHasAllowedAction_ReturnsTrue(
        string taskType,
        string authorizedAction
    )
    {
        var instance = CreateInstance(altinnTaskType: taskType);
        SetupAuthorizeAction(authorizedAction, "Task_1", true);

        Assert.True(await CreateSut().AuthorizePresentationTextsUpdate(instance));
        Assert.True(await CreateSut().AuthorizeDataValuesUpdate(instance));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("signing")]
    [InlineData("confirmation")]
    public async Task AuthorizeUpdate_UserLacksAllActions_ReturnsFalse(string taskType)
    {
        var instance = CreateInstance(altinnTaskType: taskType);

        Assert.False(await CreateSut().AuthorizePresentationTextsUpdate(instance));
        Assert.False(await CreateSut().AuthorizeDataValuesUpdate(instance));
    }

    [Fact]
    public async Task AuthorizeUpdate_NoCurrentTask_ReturnsFalse()
    {
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };

        Assert.False(await CreateSut().AuthorizePresentationTextsUpdate(instance));
        Assert.False(await CreateSut().AuthorizeDataValuesUpdate(instance));
    }

    [Fact]
    public async Task AuthorizeDataValuesUpdate_SyncAdapterScope_ReturnsTrue()
    {
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };
        _authorizationMock
            .Setup(a => a.UserHasRequiredScope("altinn:storage/instances.syncadapter"))
            .Returns(true);

        Assert.True(await CreateSut().AuthorizeDataValuesUpdate(instance));
    }

    [Fact]
    public async Task AuthorizePresentationTextsUpdate_SyncAdapterScope_ReturnsFalse()
    {
        var instance = new InstanceInternal { Process = new ProcessState { CurrentTask = null } };
        _authorizationMock
            .Setup(a => a.UserHasRequiredScope("altinn:storage/instances.syncadapter"))
            .Returns(true);

        Assert.False(await CreateSut().AuthorizePresentationTextsUpdate(instance));
    }

    [Fact]
    public async Task AuthorizeUpdate_NoSyncAdapterScope_NoActions_ReturnsFalse()
    {
        var instance = CreateInstance(altinnTaskType: "data");
        _authorizationMock
            .Setup(a => a.UserHasRequiredScope("altinn:storage/instances.syncadapter"))
            .Returns(false);

        Assert.False(await CreateSut().AuthorizePresentationTextsUpdate(instance));
        Assert.False(await CreateSut().AuthorizeDataValuesUpdate(instance));
    }

    #endregion
}
