#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class ProcessDataCleanupServiceTests
{
    private const string _appId = "ttd/test-app";
    private const string _targetTaskId = "Task_2";
    private const int _storageAccount = 7;

    /// <summary>
    /// The moment the task currently being left was entered, as recorded on the stored
    /// (pre-update) instance. Elements created before this are stale; elements created
    /// after belong to the in-flight transition and must be spared.
    /// </summary>
    private static readonly DateTime _baseline = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IDataService> _dataServiceMock = new();
    private readonly Mock<IApplicationService> _applicationServiceMock = new();

    private ProcessDataCleanupService CreateService()
    {
        _applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(_appId))
            .ReturnsAsync(
                (new Application { Id = _appId, StorageAccountNumber = _storageAccount }, null)
            );

        return new ProcessDataCleanupService(
            _dataServiceMock.Object,
            _applicationServiceMock.Object,
            NullLogger<ProcessDataCleanupService>.Instance
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NullData_ReturnsZeroAndCallsNothing()
    {
        ProcessDataCleanupService target = CreateService();
        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Data = null,
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
        _applicationServiceMock.Verify(
            s => s.GetApplicationOrErrorAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_EmptyData_ReturnsZero()
    {
        ProcessDataCleanupService target = CreateService();
        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Data = [],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NoMatches_ReturnsZero()
    {
        ProcessDataCleanupService target = CreateService();
        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data =
            [
                new DataElement { Id = Guid.NewGuid().ToString(), References = null },
                new DataElement
                {
                    Id = Guid.NewGuid().ToString(),
                    References =
                    [
                        // Wrong Relation
                        new Reference()
                        {
                            Relation = null,
                            ValueType = ReferenceType.Task,
                            Value = _targetTaskId,
                        },
                    ],
                },
                new DataElement
                {
                    Id = Guid.NewGuid().ToString(),
                    References =
                    [
                        // Wrong ValueType
                        new Reference()
                        {
                            Relation = RelationType.GeneratedFrom,
                            ValueType = ReferenceType.DataElement,
                            Value = _targetTaskId,
                        },
                    ],
                },
                new DataElement
                {
                    Id = Guid.NewGuid().ToString(),
                    References =
                    [
                        // Wrong Value (different task)
                        new Reference()
                        {
                            Relation = RelationType.GeneratedFrom,
                            ValueType = ReferenceType.Task,
                            Value = "Task_1",
                        },
                    ],
                },
            ],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        Assert.Equal(4, instance.Data.Count);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_MatchesByAllThreeFields_DeletesAndMutatesInstanceData()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement match1 = MakeStaleMatch();
        DataElement keep = new()
        {
            Id = Guid.NewGuid().ToString(),
            References =
            [
                new Reference
                {
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                    Value = "Task_other",
                },
            ],
        };
        DataElement match2 = MakeStaleMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [match1, keep, match2],
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, It.IsAny<DataElement>(), _storageAccount))
            .ReturnsAsync((Instance _, DataElement de, int? _) => de);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, deleted);
        Assert.Single(instance.Data);
        Assert.Same(keep, instance.Data[0]);

        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, match1, _storageAccount),
            Times.Once
        );
        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, match2, _storageAccount),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_DeleteThrowsForOneElement_ContinuesWithRest()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement first = MakeStaleMatch();
        DataElement failing = MakeStaleMatch();
        DataElement last = MakeStaleMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [first, failing, last],
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, first, _storageAccount))
            .ReturnsAsync(first);
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, failing, _storageAccount))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, last, _storageAccount))
            .ReturnsAsync(last);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, deleted);
        Assert.Single(instance.Data);
        Assert.Same(failing, instance.Data[0]);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_ApplicationLookupFails_Throws()
    {
        _applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(_appId))
            .ReturnsAsync(((Application)null, new ServiceError(404, "not found")));

        ProcessDataCleanupService target = new(
            _dataServiceMock.Object,
            _applicationServiceMock.Object,
            NullLogger<ProcessDataCleanupService>.Instance
        );

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [MakeStaleMatch()],
        };

        var act = () =>
            target.CleanupGeneratedFromTask(instance, _targetTaskId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_CancellationRequested_StopsBeforeNextDelete()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement first = MakeStaleMatch();
        DataElement second = MakeStaleMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [first, second],
        };

        using CancellationTokenSource cts = new();

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, first, _storageAccount))
            .Callback(cts.Cancel)
            .ReturnsAsync(first);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.CleanupGeneratedFromTask(instance, _targetTaskId, cts.Token)
        );

        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, second, It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NoProcessState_DeletesNothing()
    {
        ProcessDataCleanupService target = CreateService();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = null,
            Data = [MakeStaleMatch(), MakeMatch()],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        Assert.Equal(2, instance.Data.Count);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_ProcessNeverStarted_DeletesNothing()
    {
        ProcessDataCleanupService target = CreateService();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = new ProcessState { Started = null, CurrentTask = null },
            Data = [MakeStaleMatch()],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        Assert.Single(instance.Data);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_ElementCreatedAfterBaseline_IsSpared()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement fresh = MakeMatch(_baseline.AddSeconds(5));

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [fresh],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        Assert.Same(fresh, Assert.Single(instance.Data));
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_MixedStaleAndFresh_DeletesOnlyStale()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement stale = MakeStaleMatch();
        DataElement fresh = MakeMatch(_baseline.AddSeconds(5));

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [stale, fresh],
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, stale, _storageAccount))
            .ReturnsAsync(stale);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(1, deleted);
        Assert.Same(fresh, Assert.Single(instance.Data));
        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, stale, _storageAccount),
            Times.Once
        );
        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, fresh, It.IsAny<int?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NullCreated_TreatedAsStaleAndDeleted()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement legacy = MakeMatch(created: null);

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [legacy],
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, legacy, _storageAccount))
            .ReturnsAsync(legacy);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(1, deleted);
        Assert.Empty(instance.Data);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_FirstEntryToTask_AllElementsYoungerThanBaseline_DeletesNothing()
    {
        // First ever entry to the target task: any tagged elements were created by the
        // in-flight transition's task-start commands and must survive the process save.
        ProcessDataCleanupService target = CreateService();

        DataElement taskStart1 = MakeMatch(_baseline.AddSeconds(1));
        DataElement taskStart2 = MakeMatch(_baseline.AddSeconds(2));

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = ProcessInTaskSince(_baseline),
            Data = [taskStart1, taskStart2],
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(0, deleted);
        Assert.Equal(2, instance.Data.Count);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<Instance>(),
                    It.IsAny<DataElement>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NoCurrentTask_FallsBackToProcessStarted()
    {
        // The process has started but never entered a task (stored state). The baseline
        // falls back to Process.Started: elements created after it are spared.
        ProcessDataCleanupService target = CreateService();

        DataElement stale = MakeMatch(_baseline.AddMinutes(-5));
        DataElement fresh = MakeMatch(_baseline.AddSeconds(5));

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
            Process = new ProcessState { Started = _baseline, CurrentTask = null },
            Data = [stale, fresh],
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, stale, _storageAccount))
            .ReturnsAsync(stale);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(1, deleted);
        Assert.Same(fresh, Assert.Single(instance.Data));
    }

    private static DataElement MakeMatch(DateTime? created = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = $"ttd/test-app/instance/data/{Guid.NewGuid()}",
            Created = created,
            References =
            [
                new Reference
                {
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                    Value = _targetTaskId,
                },
            ],
        };

    private static DataElement MakeStaleMatch() => MakeMatch(_baseline.AddMinutes(-5));

    /// <summary>
    /// Process state as stored before the update: the instance is currently in a task
    /// (the one being left) that started at <see cref="_baseline"/>.
    /// </summary>
    private static ProcessState ProcessInTaskSince(DateTime started) =>
        new()
        {
            Started = started.AddMinutes(-30),
            CurrentTask = new ProcessElementInfo { ElementId = "Task_1", Started = started },
        };
}
