#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class StudioMetricsTests
{
    private static readonly DateOnly _day = new(2026, 11, 9);

    private readonly IStudioMetricsRepository _repository;

    public StudioMetricsTests()
    {
        _repository = (IStudioMetricsRepository)
            ServiceUtil.GetServices([typeof(IStudioMetricsRepository)])[0];

        string sql =
            "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    [Fact]
    public async Task GetDailyMetrics_NoInstances_ReturnsNoApps()
    {
        // Act
        StudioDailyMetrics metrics = await _repository.GetDailyMetrics(
            "ttd",
            _day,
            CancellationToken.None
        );

        // Assert
        Assert.Equal("ttd", metrics.Org);
        Assert.Equal(_day, metrics.Date);
        Assert.Empty(metrics.Apps);
    }

    [Fact]
    public async Task GetDailyMetrics_CountsFlowForTheDayAndStockByAge()
    {
        // Arrange
        // Created and still open on the day.
        await InsertOpen("ttd/kpi-a", "2026-11-09T08:00:00Z", "Task_1");

        // Created and completed on the day in ten minutes, not yet confirmed by the service owner.
        await InsertCompleted(
            "ttd/kpi-a",
            created: "2026-11-09T09:00:00Z",
            ended: "2026-11-09T09:10:00Z",
            confirmed: false
        );

        // Created the day before, completed on the day after twenty hours.
        await InsertCompleted(
            "ttd/kpi-a",
            created: "2026-11-08T10:00:00Z",
            ended: "2026-11-09T06:00:00Z",
            confirmed: true
        );

        // Open for more than 90 days.
        await InsertOpen("ttd/kpi-a", "2026-07-01T00:00:00Z", "Task_2");

        // Completed more than 90 days ago.
        await InsertCompleted(
            "ttd/kpi-a",
            created: "2026-05-01T00:00:00Z",
            ended: "2026-05-02T00:00:00Z",
            confirmed: false
        );

        // Altinn 2 instances are left out of the flow counts.
        await InsertCompleted(
            "ttd/kpi-a",
            created: "2026-11-09T12:00:00Z",
            ended: "2026-11-09T12:05:00Z",
            confirmed: false,
            altinnMainVersion: 2
        );

        // Created the next day.
        await InsertOpen("ttd/kpi-a", "2026-11-10T00:00:00Z", "Task_1");

        // Hard deleted but not yet cleaned up: still has a taskid, must not count as open.
        await InsertOpen("ttd/kpi-a", "2026-11-09T10:00:00Z", "Task_1", hardDeleted: true);

        // Soft deleted instances can be restored and still count as open.
        await InsertOpen("ttd/kpi-a", "2026-11-09T11:00:00Z", "Task_2", softDeleted: true);

        // Another app with only an old open instance.
        await InsertOpen("ttd/kpi-b", "2026-01-01T00:00:00Z", "Task_1");

        // Another service owner.
        await InsertOpen("skd/kpi-a", "2026-11-09T08:00:00Z", "Task_1");

        // Act
        StudioDailyMetrics metrics = await _repository.GetDailyMetrics(
            "ttd",
            _day,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(["kpi-a", "kpi-b"], metrics.Apps.Select(a => a.App));

        StudioAppDailyMetrics appA = metrics.Apps[0];
        Assert.Equal(4, appA.InstancesCreated);
        Assert.Equal(2, appA.InstancesCompleted);
        Assert.Equal((600 + 72000) / 2, appA.SecondsToCompleteP50);
        Assert.Equal(4, appA.OpenInstances);
        Assert.Equal(1, appA.OpenOver90Days);
        Assert.Equal(
            [("Task_1", 2L), ("Task_2", 2L)],
            appA.OpenByTask.Select(t => (t.TaskId, t.OpenInstances))
        );

        StudioAppDailyMetrics appB = metrics.Apps[1];
        Assert.Equal(0, appB.InstancesCreated);
        Assert.Equal(0, appB.InstancesCompleted);
        Assert.Null(appB.SecondsToCompleteP50);
        Assert.Equal(1, appB.OpenInstances);
        Assert.Equal(1, appB.OpenOver90Days);
        Assert.Equal([("Task_1", 1L)], appB.OpenByTask.Select(t => (t.TaskId, t.OpenInstances)));
    }

    [Fact]
    public async Task GetDailyMetrics_DayBoundariesAreUtc()
    {
        // Arrange
        await InsertOpen("ttd/kpi-a", "2026-11-08T23:59:59Z", "Task_1");
        await InsertOpen("ttd/kpi-a", "2026-11-09T00:00:00Z", "Task_1");
        await InsertOpen("ttd/kpi-a", "2026-11-09T23:59:59Z", "Task_1");
        await InsertOpen("ttd/kpi-a", "2026-11-10T00:00:00Z", "Task_1");

        // Act
        StudioDailyMetrics metrics = await _repository.GetDailyMetrics(
            "ttd",
            _day,
            CancellationToken.None
        );

        // Assert
        StudioAppDailyMetrics app = Assert.Single(metrics.Apps);
        Assert.Equal(2, app.InstancesCreated);
        Assert.Equal(4, app.OpenInstances);
    }

    private static Task InsertOpen(
        string appId,
        string created,
        string taskId,
        bool hardDeleted = false,
        bool softDeleted = false
    )
    {
        string status =
            hardDeleted
                ? $$$"""{"IsArchived": false, "IsSoftDeleted": true, "SoftDeleted": "{{{created}}}", "IsHardDeleted": true, "HardDeleted": "{{{created}}}"}"""
            : softDeleted
                ? $$$"""{"IsArchived": false, "IsSoftDeleted": true, "SoftDeleted": "{{{created}}}"}"""
            : """{"IsArchived": false}""";
        string instance =
            $$$"""{"Process": {"Started": "{{{created}}}", "CurrentTask": {"ElementId": "{{{taskId}}}"}}, "Status": {{{status}}}}""";
        return Insert(appId, instance, created, created, taskId, 3, false);
    }

    private static Task InsertCompleted(
        string appId,
        string created,
        string ended,
        bool confirmed,
        int altinnMainVersion = 3
    )
    {
        string instance =
            $$$"""{"Process": {"Started": "{{{created}}}", "Ended": "{{{ended}}}"}, "Status": {"IsArchived": true, "Archived": "{{{ended}}}"}}""";
        return Insert(appId, instance, created, ended, null, altinnMainVersion, confirmed);
    }

    private static Task Insert(
        string appId,
        string instance,
        string created,
        string lastChanged,
        string taskId,
        int altinnMainVersion,
        bool confirmed
    )
    {
        string org = appId.Split('/')[0];
        string taskIdValue = taskId is null ? "NULL" : $"'{taskId}'";
        string sql =
            "INSERT INTO storage.instances (partyid, instance, created, lastchanged, org, appid, taskid, altinnmainversion, confirmed)"
            + $" VALUES (1337, '{instance}', '{created}', '{lastChanged}', '{org}', '{appId}', {taskIdValue}, {altinnMainVersion}, {(confirmed ? "true" : "false")})";
        return PostgresUtil.RunSql(sql);
    }
}
