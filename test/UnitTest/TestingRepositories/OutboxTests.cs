#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class OutboxTests
{
    public OutboxTests()
    {
        string sql = "delete from storage.outbox";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    private static SyncInstanceToDialogportenCommand CreateCommand(
        string instanceId,
        InstanceEventType evt = InstanceEventType.Saved,
        string appId = "app",
        string partyId = "123",
        bool migration = false,
        DateTime? created = null
    ) => new(appId, partyId, instanceId, created ?? DateTime.UtcNow.AddMinutes(-5), migration, evt);

    [Fact]
    public async Task Insert_EnableSendingFalse_DoesNotInsertRow()
    {
        var cmdObj = CreateCommand(Guid.NewGuid().ToString());
        await using NpgsqlConnection connection = GetConnection();

        await InsertAndCommit(
            GetRepo(new WolverineSettings() { EnableSending = false }),
            cmdObj,
            connection
        );

        string sql = $"select count(*) from storage.outbox";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Insert_EnableSendingTrue_InsertsRow()
    {
        var cmdObj = CreateCommand(Guid.NewGuid().ToString());

        await using var connection = GetConnection();
        await InsertAndCommit(GetRepo(), cmdObj, connection);

        string sql = $"select count(*) from storage.outbox";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Insert_SecondInsertWithEarlierValidFrom_UpdatesValidFromAndKeepsEventType()
    {
        var sharedId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var first = CreateCommand(sharedId, created: now, evt: InstanceEventType.Saved);
        var second = CreateCommand(sharedId, created: now, evt: InstanceEventType.Deleted);

        var repo = GetRepo();
        await using var connection = GetConnection();
        await InsertAndCommit(repo, first, connection);
        await InsertAndCommit(repo, second, connection);

        string sql = $"select count(*) from storage.outbox";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql = $"select validfrom from storage.outbox";
        DateTime validfrom = await PostgresUtil.RunQuery<DateTime>(sql);
        sql = $"select instanceeventtype::int from storage.outbox";
        int instanceEventType = await PostgresUtil.RunQuery<int>(sql);
        var diff = validfrom - now;
        Assert.Equal(1, count);
        Assert.True(diff.TotalSeconds < 2); // Less than the delay given for Saved event
        Assert.Equal((int)InstanceEventType.Saved, instanceEventType);
    }

    [Fact]
    public async Task Insert_SecondInsertWithLaterValidFrom_DoesNotReplaceUrgentEventType()
    {
        var sharedId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var first = CreateCommand(sharedId, created: now, evt: InstanceEventType.Deleted);
        var second = CreateCommand(sharedId, created: now, evt: InstanceEventType.Saved);
        await using NpgsqlConnection connection = GetConnection();

        await InsertAndCommit(GetRepo(), first, connection);
        await InsertAndCommit(GetRepo(), second, connection);
        var dps = await GetRepo().Poll(10);

        SyncInstanceToDialogportenCommand dp = Assert.Single(dps);
        Assert.Equal(InstanceEventType.Deleted, dp.EventType);
    }

    [Fact]
    public async Task Poll_OneWithDuplicatesAndOneSingle_Returns_2()
    {
        var sharedId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var first = CreateCommand(sharedId, created: now, evt: InstanceEventType.Created);
        var second = CreateCommand(sharedId, created: now, evt: InstanceEventType.Deleted);
        var third = CreateCommand(
            Guid.NewGuid().ToString(),
            created: now,
            evt: InstanceEventType.Created
        );

        var repo = GetRepo();
        await using var connection = GetConnection();
        await InsertAndCommit(repo, first, connection);
        await InsertAndCommit(repo, second, connection);
        await InsertAndCommit(repo, third, connection);
        var dps = await repo.Poll(10);

        Assert.Equal(2, dps.Count);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        var cmdObj = CreateCommand(Guid.NewGuid().ToString());

        var repo = GetRepo();
        await using var connection = GetConnection();
        await InsertAndCommit(repo, cmdObj, connection);
        await repo.Delete(Guid.Parse(cmdObj.InstanceId));

        string sql = $"select count(*) from storage.outbox";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Insert_WithTransaction_RollsBackWithTransaction()
    {
        var cmdObj = CreateCommand(Guid.NewGuid().ToString());
        await using NpgsqlConnection connection = GetConnection();
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync();

        await GetRepo().Insert(cmdObj, connection, tx);

        await using var countCommand = new NpgsqlCommand(
            "select count(*) from storage.outbox",
            connection,
            tx
        );
        int countBeforeRollback = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        Assert.Equal(1, countBeforeRollback);

        await tx.RollbackAsync();

        string sql = $"select count(*) from storage.outbox";
        int countAfterRollback = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, countAfterRollback);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_RespectsExistingLease()
    {
        var resource = "r1";
        var holder = Guid.NewGuid();
        var holder2 = Guid.NewGuid();

        var repo = GetRepo();

        // First acquire
        var ok1 = await repo.TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(2));
        Assert.True(ok1);

        // Second acquire by different holder before expiry should fail
        var ok2 = await repo.TryAcquireLeaseAsync(resource, holder2, DateTime.UtcNow.AddSeconds(2));
        Assert.False(ok2);

        // Wait until expired
        await Task.Delay(2100);
        var ok3 = await repo.TryAcquireLeaseAsync(resource, holder2, DateTime.UtcNow.AddSeconds(2));
        Assert.True(ok3);
    }

    [Fact]
    public async Task RenewLeaseAsync_ExtendsLease()
    {
        var resource = "r2";
        var holder = Guid.NewGuid();

        var repo = GetRepo();

        var ok1 = await repo.TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(1));
        Assert.True(ok1);

        var renewed = await repo.RenewLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(3));
        Assert.True(renewed);

        // Should still block other holder
        var other = await repo.TryAcquireLeaseAsync(
            resource,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(1)
        );
        Assert.False(other);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_RemovesLease()
    {
        var resource = "r3";
        var holder = Guid.NewGuid();

        var repo = GetRepo();

        Assert.True(
            await repo.TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(5))
        );

        var released = await repo.ReleaseLeaseAsync(resource, holder);
        Assert.True(released);

        // Now acquisition by someone else should succeed immediately
        var ok = await repo.TryAcquireLeaseAsync(
            resource,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(2)
        );
        Assert.True(ok);
    }

    private static IOutboxRepository GetRepo(
        WolverineSettings wolverineSettings = null,
        string path = "/storage/api/v1/instances"
    )
    {
        wolverineSettings ??= new WolverineSettings() { EnableSending = true };
        var serviceList = GetServices([typeof(IOutboxRepository)], wolverineSettings, path);
        return (IOutboxRepository)serviceList.First(i => i.GetType() == typeof(PgOutboxRepository));
    }

    private static NpgsqlConnection GetConnection()
    {
        return ServiceUtil.GetSharedDataSource().OpenConnection();
    }

    private static async Task InsertAndCommit(
        IOutboxRepository repository,
        SyncInstanceToDialogportenCommand command,
        NpgsqlConnection connection
    )
    {
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync();
        await repository.Insert(command, connection, tx);
        await tx.CommitAsync();
    }

    private static List<object> GetServices(
        List<Type> interfaceTypes,
        WolverineSettings wolverineSettings,
        string path
    )
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(ServiceUtil.GetAppsettingsPath())
            .AddEnvironmentVariables();

        var config = builder.Build();

        IServiceCollection services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton(ServiceUtil.GetSharedDataSource());
        services.AddRepositoryImplementations();
        services.AddMemoryCache();

        Mock<IHttpContextAccessor> httpContextAccessor = new Mock<IHttpContextAccessor>(
            MockBehavior.Strict
        );
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns(httpContext);
        services.AddSingleton(httpContextAccessor.Object);

        services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));
        services.Configure<WolverineSettings>(opts =>
        {
            opts.LowPriorityDelaySecs = wolverineSettings.LowPriorityDelaySecs;
            opts.UrgentPriorityDelaySecs = wolverineSettings.UrgentPriorityDelaySecs;
            opts.HighPriorityDelaySecs = wolverineSettings.HighPriorityDelaySecs;
            opts.PollErrorDelayMs = wolverineSettings.PollErrorDelayMs;
            opts.PollMaxSize = wolverineSettings.PollMaxSize;
            opts.EnableSending = wolverineSettings.EnableSending;
        });
        var serviceProvider = services.BuildServiceProvider();
        List<object> outputServices = [];

        foreach (Type interfaceType in interfaceTypes)
        {
            var outputServiceObject = serviceProvider.GetServices(interfaceType)!;
            outputServices.AddRange(outputServiceObject!);
        }

        return outputServices;
    }
}
