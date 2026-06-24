#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task CleanupGeneratedFromTask_NullDataElements_ReturnsSameInstanceAndCallsNothing()
    {
        ProcessDataCleanupService target = CreateService();
        InstanceInternal instanceInternal = new(
            new Instance
            {
                Id = "1/abc",
                AppId = _appId,
                Data = null,
            },
            null
        );

        InstanceInternal cleanedInstance = await target.CleanupGeneratedFromTask(
            instanceInternal,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Same(instanceInternal, cleanedInstance);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
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
    public async Task CleanupGeneratedFromTask_EmptyDataElements_ReturnsSameInstanceAndCallsNothing()
    {
        ProcessDataCleanupService target = CreateService();
        InstanceInternal instanceInternal = MakeInstanceInternal();

        InstanceInternal cleanedInstance = await target.CleanupGeneratedFromTask(
            instanceInternal,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Same(instanceInternal, cleanedInstance);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_NoMatches_ReturnsSameInstanceAndCallsNothing()
    {
        ProcessDataCleanupService target = CreateService();
        DataElementInternal noReferences = MakeDataElementInternal();
        DataElementInternal wrongRelation = MakeDataElementInternal(
            new Reference
            {
                Relation = null,
                ValueType = ReferenceType.Task,
                Value = _targetTaskId,
            }
        );
        DataElementInternal wrongValueType = MakeDataElementInternal(
            new Reference
            {
                Relation = RelationType.GeneratedFrom,
                ValueType = ReferenceType.DataElement,
                Value = _targetTaskId,
            }
        );
        DataElementInternal wrongTask = MakeDataElementInternal(
            new Reference
            {
                Relation = RelationType.GeneratedFrom,
                ValueType = ReferenceType.Task,
                Value = "Task_1",
            }
        );
        InstanceInternal instanceInternal = MakeInstanceInternal(
            noReferences,
            wrongRelation,
            wrongValueType,
            wrongTask
        );

        InstanceInternal cleanedInstance = await target.CleanupGeneratedFromTask(
            instanceInternal,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Same(instanceInternal, cleanedInstance);
        Assert.Equal(4, cleanedInstance.DataElements.Count);
        Assert.Equal(4, cleanedInstance.Instance.Data.Count);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>(),
                    It.IsAny<int?>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_MatchesByAllThreeFields_DeletesAndMutatesInstanceData()
    {
        ProcessDataCleanupService target = CreateService();

        DataElementInternal match1 = MakeMatch();
        DataElementInternal keep = MakeDataElementInternal(
            new Reference
            {
                Relation = RelationType.GeneratedFrom,
                ValueType = ReferenceType.Task,
                Value = "Task_other",
            }
        );
        DataElementInternal match2 = MakeMatch();
        InstanceInternal instanceInternal = MakeInstanceInternal(match1, keep, match2);
        int originalDataElementCount = instanceInternal.Instance.Data.Count;
        int originalInternalDataElementCount = instanceInternal.DataElements.Count;

        _dataServiceMock
            .Setup(d =>
                d.DeleteImmediately(
                    instanceInternal,
                    It.IsAny<DataElementInternal>(),
                    _storageAccount
                )
            )
            .Returns(Task.CompletedTask);

        InstanceInternal cleanedInstance = await target.CleanupGeneratedFromTask(
            instanceInternal,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, originalInternalDataElementCount - cleanedInstance.DataElements.Count);
        Assert.Equal(2, originalDataElementCount - cleanedInstance.Instance.Data.Count);
        Assert.Same(instanceInternal.Instance, cleanedInstance.Instance);
        DataElementInternal remainingElement = Assert.Single(cleanedInstance.DataElements);
        Assert.Same(keep, remainingElement);
        DataElement responseDataElement = Assert.Single(cleanedInstance.Instance.Data);
        Assert.Same(keep.DataElement, responseDataElement);
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    instanceInternal,
                    It.Is<DataElementInternal>(d => d == match1),
                    _storageAccount
                ),
            Times.Once
        );
        _dataServiceMock.Verify(
            d =>
                d.DeleteImmediately(
                    instanceInternal,
                    It.Is<DataElementInternal>(d => d == match2),
                    _storageAccount
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_DeleteThrowsForOneElement_ContinuesWithRest()
    {
        ProcessDataCleanupService target = CreateService();

        DataElementInternal first = MakeMatch();
        DataElementInternal failing = MakeMatch();
        DataElementInternal last = MakeMatch();
        InstanceInternal instanceInternal = MakeInstanceInternal(first, failing, last);
        int originalDataElementCount = instanceInternal.Instance.Data.Count;
        int originalInternalDataElementCount = instanceInternal.DataElements.Count;

        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instanceInternal, first, _storageAccount))
            .Returns(Task.CompletedTask);
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instanceInternal, failing, _storageAccount))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instanceInternal, last, _storageAccount))
            .Returns(Task.CompletedTask);

        InstanceInternal cleanedInstance = await target.CleanupGeneratedFromTask(
            instanceInternal,
            _targetTaskId,
            CancellationToken.None
        );

        Assert.Equal(2, originalInternalDataElementCount - cleanedInstance.DataElements.Count);
        Assert.Equal(2, originalDataElementCount - cleanedInstance.Instance.Data.Count);
        Assert.Same(instanceInternal.Instance, cleanedInstance.Instance);
        DataElementInternal remainingElement = Assert.Single(cleanedInstance.DataElements);
        Assert.Same(failing, remainingElement);
        DataElement responseDataElement = Assert.Single(cleanedInstance.Instance.Data);
        Assert.Same(failing.DataElement, responseDataElement);
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

        InstanceInternal instanceInternal = MakeInstanceInternal(MakeMatch());

        var act = () =>
            target.CleanupGeneratedFromTask(
                instanceInternal,
                _targetTaskId,
                CancellationToken.None
            );

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task CleanupGeneratedFromTask_CancellationRequested_StopsBeforeNextDelete()
    {
        ProcessDataCleanupService target = CreateService();

        DataElementInternal first = MakeMatch();
        DataElementInternal second = MakeMatch();
        InstanceInternal instanceInternal = MakeInstanceInternal(first, second);

        using CancellationTokenSource cts = new();
        _dataServiceMock
            .Setup(d => d.DeleteImmediately(instanceInternal, first, _storageAccount))
            .Callback(cts.Cancel)
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.CleanupGeneratedFromTask(instanceInternal, _targetTaskId, cts.Token)
        );

        _dataServiceMock.Verify(
            d => d.DeleteImmediately(instanceInternal, second, It.IsAny<int?>()),
            Times.Never
        );
    }

    private static DataElementInternal MakeMatch() =>
        MakeDataElementInternal(
            new Reference
            {
                Relation = RelationType.GeneratedFrom,
                ValueType = ReferenceType.Task,
                Value = _targetTaskId,
            }
        );

    private static DataElementInternal MakeDataElementInternal(params Reference[] references) =>
        new(
            new DataElement
            {
                Id = Guid.NewGuid().ToString(),
                BlobStoragePath = $"ttd/test-app/instance/data/{Guid.NewGuid()}",
                References = references.Length == 0 ? null : new List<Reference>(references),
            },
            null
        );

    private static InstanceInternal MakeInstanceInternal(
        params DataElementInternal[] dataElements
    ) =>
        new(
            new Instance
            {
                Id = "1/abc",
                AppId = _appId,
                Data = dataElements.Select(dataElement => dataElement.DataElement).ToList(),
            },
            dataElements
        );
}
