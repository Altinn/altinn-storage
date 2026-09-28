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

        DataElement match1 = MakeMatch();
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
        DataElement match2 = MakeMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
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

        DataElement first = MakeMatch();
        DataElement failing = MakeMatch();
        DataElement last = MakeMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
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
            Data = [MakeMatch()],
        };

        var act = () =>
            target.CleanupGeneratedFromTask(instance, _targetTaskId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_CancellationRequested_StopsBeforeNextDelete()
    {
        ProcessDataCleanupService target = CreateService();

        DataElement first = MakeMatch();
        DataElement second = MakeMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = _appId,
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

    private static DataElement MakeMatch() =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            BlobStoragePath = $"ttd/test-app/instance/data/{Guid.NewGuid()}",
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
}
