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
    private const string AppId = "ttd/test-app";
    private const string TargetTaskId = "Task_2";
    private const int StorageAccount = 7;

    private readonly Mock<IDataService> _dataServiceMock = new();
    private readonly Mock<IApplicationService> _applicationServiceMock = new();

    private ProcessDataCleanupService CreateService()
    {
        _applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(AppId))
            .ReturnsAsync(
                (new Application { Id = AppId, StorageAccountNumber = StorageAccount }, null)
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
            AppId = AppId,
            Data = null,
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
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
            AppId = AppId,
            Data = new List<DataElement>(),
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
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
            AppId = AppId,
            Data = new List<DataElement>
            {
                new() { Id = Guid.NewGuid().ToString(), References = null },
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    References = new List<Reference>
                    {
                        // Wrong Relation
                        new()
                        {
                            Relation = null,
                            ValueType = ReferenceType.Task,
                            Value = TargetTaskId,
                        },
                    },
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    References = new List<Reference>
                    {
                        // Wrong ValueType
                        new()
                        {
                            Relation = RelationType.GeneratedFrom,
                            ValueType = ReferenceType.DataElement,
                            Value = TargetTaskId,
                        },
                    },
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    References = new List<Reference>
                    {
                        // Wrong Value (different task)
                        new()
                        {
                            Relation = RelationType.GeneratedFrom,
                            ValueType = ReferenceType.Task,
                            Value = "Task_1",
                        },
                    },
                },
            },
        };

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
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
            References = new List<Reference>
            {
                new()
                {
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                    Value = "Task_other",
                },
            },
        };
        DataElement match2 = MakeMatch();

        Instance instance = new()
        {
            Id = "1/abc",
            AppId = AppId,
            Data = new List<DataElement> { match1, keep, match2 },
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, It.IsAny<DataElement>(), StorageAccount))
            .ReturnsAsync((Instance _, DataElement de, int? _) => de);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, deleted);
        Assert.Single(instance.Data);
        Assert.Same(keep, instance.Data[0]);

        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, match1, StorageAccount),
            Times.Once
        );
        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instance, match2, StorageAccount),
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
            AppId = AppId,
            Data = new List<DataElement> { first, failing, last },
        };

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, first, StorageAccount))
            .ReturnsAsync(first);
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, failing, StorageAccount))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, last, StorageAccount))
            .ReturnsAsync(last);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, deleted);
        Assert.Single(instance.Data);
        Assert.Same(failing, instance.Data[0]);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_ApplicationLookupFails_StillProceedsWithDefaultStorageAccount()
    {
        // Application lookup returns null; cleanup should still attempt deletes,
        // passing null for storageAccountNumber.
        _applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(AppId))
            .ReturnsAsync(((Application)null, new ServiceError(404, "not found")));

        ProcessDataCleanupService target = new(
            _dataServiceMock.Object,
            _applicationServiceMock.Object,
            NullLogger<ProcessDataCleanupService>.Instance
        );

        DataElement match = MakeMatch();
        Instance instance = new()
        {
            Id = "1/abc",
            AppId = AppId,
            Data = new List<DataElement> { match },
        };

        _dataServiceMock.Setup(d => d.DeleteImmediately(instance, match, null)).ReturnsAsync(match);

        int deleted = await target.CleanupGeneratedFromTask(
            instance,
            TargetTaskId,
            CancellationToken.None
        );

        Assert.Equal(1, deleted);
        _dataServiceMock.Verify(d => d.DeleteImmediately(instance, match, null), Times.Once);
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
            AppId = AppId,
            Data = new List<DataElement> { first, second },
        };

        using CancellationTokenSource cts = new();

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instance, first, StorageAccount))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(first);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.CleanupGeneratedFromTask(instance, TargetTaskId, cts.Token)
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
            BlobStoragePath = "ttd/test-app/instance/data/" + Guid.NewGuid(),
            References = new List<Reference>
            {
                new()
                {
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                    Value = TargetTaskId,
                },
            },
        };
}
