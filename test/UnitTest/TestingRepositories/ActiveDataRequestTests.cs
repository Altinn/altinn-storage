#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class ActiveDataRequestTests(ActiveDataRequestFixture fixture)
    : IClassFixture<ActiveDataRequestFixture>,
        IAsyncLifetime
{
    private readonly ActiveDataRequestFixture _fixture = fixture;

    public async Task InitializeAsync()
    {
        string sql = """
            delete from storage.activedatarequests;
            delete from storage.instancelocks;
            delete from storage.instances;
            delete from storage.dataelements;
            """;

        await PostgresUtil.RunSql(sql);
    }

    public async Task DisposeAsync()
    {
        await PostgresUtil.UnfreezeTime();
    }

    /// <summary>
    /// Test that BeginDataMutation succeeds for an existing instance with no active lock.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_NoLock_ReturnsOk()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        var timeout = TimeSpan.FromSeconds(300);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.Ok, status);
        Assert.NotNull(requestId);
    }

    /// <summary>
    /// Test that BeginDataMutation returns InstanceNotFound for a non-existent instance.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_InstanceNotFound_ReturnsInstanceNotFound()
    {
        // Arrange
        var nonExistentGuid = Guid.NewGuid();
        var timeout = TimeSpan.FromSeconds(300);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            nonExistentGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.InstanceNotFound, status);
        Assert.Null(requestId);
    }

    /// <summary>
    /// Test that BeginDataMutation is blocked when a preventMutations lock is active and no token is provided.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_PreventMutationsLock_NoToken_ReturnsMutationBlocked()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(300);

        // Acquire a lock with preventMutations=true
        var (lockResult, _) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            true,
            "123"
        );
        Assert.Equal(AcquireLockResult.Success, lockResult);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.MutationBlocked, status);
        Assert.Null(requestId);
    }

    /// <summary>
    /// Test that BeginDataMutation is blocked when a preventMutations lock is active and a wrong token is provided.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_PreventMutationsLock_WrongToken_ReturnsMutationBlocked()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(300);

        // Acquire a lock with preventMutations=true
        var (lockResult, _) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            true,
            "123"
        );
        Assert.Equal(AcquireLockResult.Success, lockResult);

        // Create a fake lock token with wrong secret
        var wrongToken = new LockToken(999, new byte[20]);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            wrongToken,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.MutationBlocked, status);
        Assert.Null(requestId);
    }

    /// <summary>
    /// Test that BeginDataMutation succeeds when a preventMutations lock is active and the correct token is provided.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_PreventMutationsLock_CorrectToken_ReturnsOk()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(300);

        // Acquire a lock with preventMutations=true
        var (lockResult, lockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            true,
            "123"
        );
        Assert.Equal(AcquireLockResult.Success, lockResult);
        Assert.NotNull(lockToken);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            lockToken,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.Ok, status);
        Assert.NotNull(requestId);
    }

    /// <summary>
    /// Test that BeginDataMutation succeeds when a lock exists but preventMutations is false.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_LockWithoutPreventMutations_ReturnsOk()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(300);

        // Acquire a lock with preventMutations=false
        var (lockResult, _) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            false,
            "123"
        );
        Assert.Equal(AcquireLockResult.Success, lockResult);

        // Act
        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.Ok, status);
        Assert.NotNull(requestId);
    }

    /// <summary>
    /// Test that EndDataMutation removes the tracking row.
    /// </summary>
    [Fact]
    public async Task EndDataMutation_RemovesTrackingRow()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        var timeout = TimeSpan.FromSeconds(300);

        var (status, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        Assert.Equal(BeginMutationStatus.Ok, status);
        Assert.NotNull(requestId);

        var countBefore = await PostgresUtil.RunCountQuery(
            "SELECT count(*) FROM storage.activedatarequests"
        );
        Assert.Equal(1, countBefore);

        // Act
        await _fixture.ActiveDataRequestRepo.EndDataMutation(requestId.Value);

        // Assert
        var countAfter = await PostgresUtil.RunCountQuery(
            "SELECT count(*) FROM storage.activedatarequests"
        );
        Assert.Equal(0, countAfter);
    }

    /// <summary>
    /// Test that multiple concurrent mutations are allowed (multiple BeginDataMutation calls succeed).
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_MultipleConcurrent_AllSucceed()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        var timeout = TimeSpan.FromSeconds(300);

        // Act
        var (status1, requestId1) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        var (status2, requestId2) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        var (status3, requestId3) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.Ok, status1);
        Assert.Equal(BeginMutationStatus.Ok, status2);
        Assert.Equal(BeginMutationStatus.Ok, status3);
        Assert.NotNull(requestId1);
        Assert.NotNull(requestId2);
        Assert.NotNull(requestId3);
        Assert.NotEqual(requestId1, requestId2);
        Assert.NotEqual(requestId2, requestId3);

        var count = await PostgresUtil.RunCountQuery(
            "SELECT count(*) FROM storage.activedatarequests"
        );
        Assert.Equal(3, count);
    }

    /// <summary>
    /// Test that lock acquisition is blocked while active data requests exist.
    /// </summary>
    [Fact]
    public async Task AcquireLock_BlockedByActiveDataRequests()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(300);

        // Begin a data mutation
        var (mutationStatus, requestId) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        Assert.Equal(BeginMutationStatus.Ok, mutationStatus);

        // Act - try to acquire a lock while mutation is active
        var (lockResult, lockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            false,
            "123"
        );

        // Assert
        Assert.Equal(AcquireLockResult.ActiveRequestsInProgress, lockResult);
        Assert.Null(lockToken);

        // End the mutation and try again
        await _fixture.ActiveDataRequestRepo.EndDataMutation(requestId!.Value);

        var (lockResult2, lockToken2) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            false,
            "123"
        );

        Assert.Equal(AcquireLockResult.Success, lockResult2);
        Assert.NotNull(lockToken2);
    }

    /// <summary>
    /// Test that expired tracking rows are cleaned up during BeginDataMutation.
    /// </summary>
    [Fact]
    public async Task BeginDataMutation_CleansUpExpiredRows()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        var timeout = TimeSpan.FromSeconds(60);

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Begin a data mutation that will expire
        var (status1, requestId1) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        Assert.Equal(BeginMutationStatus.Ok, status1);

        var countBefore = await PostgresUtil.RunCountQuery(
            "SELECT count(*) FROM storage.activedatarequests"
        );
        Assert.Equal(1, countBefore);

        // Advance time past the timeout
        await PostgresUtil.FreezeTime(startTime.AddSeconds(60));

        // Act - begin another mutation, which should clean up the expired row
        var (status2, requestId2) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );

        // Assert
        Assert.Equal(BeginMutationStatus.Ok, status2);
        Assert.NotNull(requestId2);

        // Only the new row should remain (expired row cleaned up)
        var countAfter = await PostgresUtil.RunCountQuery(
            "SELECT count(*) FROM storage.activedatarequests"
        );
        Assert.Equal(1, countAfter);
    }

    /// <summary>
    /// Test that expired tracking rows are cleaned up during lock acquisition,
    /// allowing the lock to be acquired.
    /// </summary>
    [Fact]
    public async Task AcquireLock_CleansUpExpiredDataRequests_ThenSucceeds()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        var instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        var timeout = TimeSpan.FromSeconds(60);

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Begin a data mutation
        var (mutationStatus, _) = await _fixture.ActiveDataRequestRepo.BeginDataMutation(
            instanceGuid,
            null,
            timeout
        );
        Assert.Equal(BeginMutationStatus.Ok, mutationStatus);

        // Lock should be blocked by active request
        var (lockResult1, _) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            false,
            "123"
        );
        Assert.Equal(AcquireLockResult.ActiveRequestsInProgress, lockResult1);

        // Advance time past the data request timeout
        await PostgresUtil.FreezeTime(startTime.AddSeconds(60));

        // Act - lock should succeed now because expired rows are cleaned up
        var (lockResult2, lockToken2) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            300,
            false,
            "123"
        );

        // Assert
        Assert.Equal(AcquireLockResult.Success, lockResult2);
        Assert.NotNull(lockToken2);
    }
}

public class ActiveDataRequestFixture
{
    public IActiveDataRequestRepository ActiveDataRequestRepo { get; set; }

    public IInstanceLockRepository InstanceLockRepo { get; set; }

    public IInstanceRepository InstanceRepo { get; set; }

    public ActiveDataRequestFixture()
    {
        var serviceList = ServiceUtil.GetServices([
            typeof(IActiveDataRequestRepository),
            typeof(IInstanceLockRepository),
            typeof(IInstanceRepository),
        ]);
        ActiveDataRequestRepo = (IActiveDataRequestRepository)
            serviceList.First(i => i.GetType() == typeof(PgActiveDataRequestRepository));
        InstanceLockRepo = (IInstanceLockRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceLockRepository));
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
    }
}
