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
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class ProcessDataCleanupServiceTests
{
    private const string _appId = "ttd/test-app";
    private const string _targetTaskId = "Task_2";

    private static ProcessDataCleanupService CreateService() =>
        new(NullLogger<ProcessDataCleanupService>.Instance);

    [Fact]
    public async Task GetGeneratedFromTaskDataElements_NullDataElements_ReturnsEmptyList()
    {
        ProcessDataCleanupService target = CreateService();
        InstanceInternal instance = new()
        {
            Id = "abc",
            AppId = _appId,
            Data = null,
        };

        IReadOnlyList<DataElementInternal> dataElements =
            await target.GetGeneratedFromTaskDataElements(
                instance,
                _targetTaskId,
                CancellationToken.None
            );

        Assert.Empty(dataElements);
    }

    [Fact]
    public async Task GetGeneratedFromTaskDataElements_EmptyDataElements_ReturnsEmptyList()
    {
        ProcessDataCleanupService target = CreateService();
        InstanceInternal instance = MakeInstanceInternal();

        IReadOnlyList<DataElementInternal> dataElements =
            await target.GetGeneratedFromTaskDataElements(
                instance,
                _targetTaskId,
                CancellationToken.None
            );

        Assert.Empty(dataElements);
    }

    [Fact]
    public async Task GetGeneratedFromTaskDataElements_NoMatches_ReturnsEmptyList()
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
        InstanceInternal instance = MakeInstanceInternal(
            noReferences,
            wrongRelation,
            wrongValueType,
            wrongTask
        );

        IReadOnlyList<DataElementInternal> dataElements =
            await target.GetGeneratedFromTaskDataElements(
                instance,
                _targetTaskId,
                CancellationToken.None
            );

        Assert.Empty(dataElements);
        Assert.Equal(4, instance.Data.Count);
    }

    [Fact]
    public async Task GetGeneratedFromTaskDataElements_MatchesByAllThreeFields_ReturnsDataElementsWithoutMutatingInstance()
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
        InstanceInternal instance = MakeInstanceInternal(match1, keep, match2);

        IReadOnlyList<DataElementInternal> dataElements =
            await target.GetGeneratedFromTaskDataElements(
                instance,
                _targetTaskId,
                CancellationToken.None
            );

        Assert.Equal([match1, match2], dataElements);
        Assert.Equal(3, instance.Data.Count);
        Assert.Contains(keep, instance.Data);
    }

    [Fact]
    public async Task GetGeneratedFromTaskDataElements_CancellationRequested_ThrowsBeforeReturningMatches()
    {
        ProcessDataCleanupService target = CreateService();
        InstanceInternal instance = MakeInstanceInternal(MakeMatch());
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.GetGeneratedFromTaskDataElements(instance, _targetTaskId, cts.Token)
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

    private static DataElementInternal MakeDataElementInternal(params Reference[] references)
    {
        string dataElementId = Guid.NewGuid().ToString();
        return new DataElementInternal
        {
            Id = dataElementId,
            BlobStoragePath = $"ttd/test-app/instance/data/{dataElementId}",
            References = references.Length == 0 ? null : new List<Reference>(references),
        };
    }

    private static InstanceInternal MakeInstanceInternal(
        params DataElementInternal[] dataElements
    ) =>
        new()
        {
            Id = "5f857f25-04a4-4c70-913b-cb40e2a65428",
            AppId = _appId,
            Org = "ttd",
            Data = dataElements.ToList(),
            Versions = new StorageVersions(1, 1),
        };
}
