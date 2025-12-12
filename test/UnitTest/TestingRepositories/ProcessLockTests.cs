#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class ProcessLockTests(ProcessLockFixture fixture)
    : IClassFixture<ProcessLockFixture>,
        IAsyncLifetime
{
    private readonly ProcessLockFixture _fixture = fixture;

    public async Task InitializeAsync()
    {
        string sql = """
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
    /// Test that acquiring a lock while one is active fails, but succeeds after expiration
    /// </summary>
    [Fact]
    public async Task TryAcquireLock_WhenLockActive_ReturnsLockAlreadyHeld_WhenExpired_ReturnsSuccess()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        var ttlSeconds = 300;
        var userId = "123";

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Act
        var (firstResult, firstLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var (secondResult, secondLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(30));

        var (thirdResult, thirdLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds));

        var (fourthResult, fourthLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId!.Value);
        var fourthLock = await _fixture.ProcessLockRepo.Get(fourthLockId!.Value);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotEqual(Guid.Empty, firstLockId);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, secondResult);
        Assert.Null(secondLockId);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, thirdResult);
        Assert.Null(thirdLockId);
        Assert.Equal(AcquireLockResult.Success, fourthResult);
        Assert.NotEqual(Guid.Empty, fourthLockId);

        Assert.NotNull(firstLock);
        Assert.NotNull(fourthLock);

        Assert.Equal(firstLockId, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), firstLock.LockedUntil);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(fourthLockId, fourthLock.Id);
        Assert.Equal(instanceInternalId, fourthLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), fourthLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds), fourthLock.LockedUntil);
        Assert.Equal(userId, fourthLock.LockedBy);

        var rowCount = await PostgresUtil.RunCountQuery(
            """
            SELECT count(*)
            FROM storage.instancelocks
            """
        );
        Assert.Equal(2, rowCount);
    }

    /// <summary>
    /// Test that releasing locks allows new locks to be acquired immediately
    /// </summary>
    [Fact]
    public async Task TryAcquireLock_AfterReleasingLock_AllowsNewLockAcquisition()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        var ttlSeconds = 300;
        var userId = "123";

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Act
        var (firstResult, firstLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLockReleased = await _fixture.ProcessLockRepo.TryUpdateLockExpiration(
            firstLockId!.Value,
            instanceInternalId,
            0
        );

        var (secondResult, secondLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(2));

        var secondLockReleased = await _fixture.ProcessLockRepo.TryUpdateLockExpiration(
            secondLockId!.Value,
            instanceInternalId,
            0
        );

        var (thirdResult, thirdLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId.Value);
        var secondLock = await _fixture.ProcessLockRepo.Get(secondLockId.Value);
        var thirdLock = await _fixture.ProcessLockRepo.Get(thirdLockId!.Value);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotNull(firstLockId);
        Assert.NotEqual(Guid.Empty, firstLockId);
        Assert.Equal(AcquireLockResult.Success, secondResult);
        Assert.NotNull(secondLockId);
        Assert.NotEqual(Guid.Empty, secondLockId.Value);
        Assert.Equal(AcquireLockResult.Success, thirdResult);
        Assert.NotNull(thirdLockId);
        Assert.NotEqual(Guid.Empty, thirdLockId.Value);

        Assert.Equal(UpdateLockResult.Success, firstLockReleased);
        Assert.Equal(UpdateLockResult.Success, secondLockReleased);

        Assert.NotNull(firstLock);
        Assert.NotNull(secondLock);
        Assert.NotNull(thirdLock);

        Assert.Equal(firstLockId, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime, firstLock.LockedUntil);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(secondLockId, secondLock.Id);
        Assert.Equal(instanceInternalId, secondLock.InstanceInternalId);
        Assert.Equal(startTime, secondLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2), secondLock.LockedUntil);
        Assert.Equal(userId, secondLock.LockedBy);

        Assert.Equal(thirdLockId, thirdLock.Id);
        Assert.Equal(instanceInternalId, thirdLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(2), thirdLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 + ttlSeconds), thirdLock.LockedUntil);
        Assert.Equal(userId, thirdLock.LockedBy);

        var rowCount = await PostgresUtil.RunCountQuery(
            """
            SELECT count(*)
            FROM storage.instancelocks
            """
        );
        Assert.Equal(3, rowCount);
    }

    /// <summary>
    /// Test that updating lock expiration extends the lock and prevents acquisition until the extended expiration
    /// </summary>
    [Fact]
    public async Task UpdateLockExpiration_ExtendsLock_PreventsAcquisitionUntilExpired()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        var ttlSeconds = 300;
        var userId = "123";

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Act
        var (firstResult, firstLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds - 1));

        var firstLockUpdated = await _fixture.ProcessLockRepo.TryUpdateLockExpiration(
            firstLockId!.Value,
            instanceInternalId,
            ttlSeconds
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds + 1));

        var (secondResult, secondLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(2 * ttlSeconds - 1));

        var (thirdResult, thirdLockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId.Value);
        var thirdLock = await _fixture.ProcessLockRepo.Get(thirdLockId!.Value);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotNull(firstLockId);
        Assert.NotEqual(Guid.Empty, firstLockId.Value);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, secondResult);
        Assert.Null(secondLockId);
        Assert.Equal(AcquireLockResult.Success, thirdResult);
        Assert.NotNull(thirdLockId);
        Assert.NotEqual(Guid.Empty, thirdLockId.Value);

        Assert.Equal(UpdateLockResult.Success, firstLockUpdated);

        Assert.NotNull(firstLock);
        Assert.NotNull(thirdLock);

        Assert.Equal(firstLockId, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds - 1), firstLock.LockedUntil);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(thirdLockId, thirdLock.Id);
        Assert.Equal(instanceInternalId, thirdLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds - 1), thirdLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(3 * ttlSeconds - 1), thirdLock.LockedUntil);
        Assert.Equal(userId, thirdLock.LockedBy);

        var rowCount = await PostgresUtil.RunCountQuery(
            """
            SELECT count(*)
            FROM storage.instancelocks
            """
        );
        Assert.Equal(2, rowCount);
    }

    /// <summary>
    /// Test that updating a non-existent lock returns LockNotFound
    /// </summary>
    [Fact]
    public async Task UpdateExpiration_Fails_WhenLockDoesNotExist()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        var nonExistentLockId = Guid.NewGuid();
        var ttlSeconds = 300;

        // Act
        var result = await _fixture.ProcessLockRepo.TryUpdateLockExpiration(
            nonExistentLockId,
            instanceInternalId,
            ttlSeconds
        );

        // Assert
        Assert.Equal(UpdateLockResult.LockNotFound, result);
    }

    /// <summary>
    /// Test that updating lock expiration fails when the lock has expired
    /// </summary>
    [Fact]
    public async Task UpdateExpiration_Fails_WhenLockIsExpired()
    {
        // Arrange
        var instance = TestData.Instance_1_1.Clone();
        instance = await _fixture.InstanceRepo.Create(instance, CancellationToken.None);
        (_, long instanceInternalId) = await _fixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        var ttlSeconds = 300;
        var userId = "123";

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        await PostgresUtil.FreezeTime(startTime);

        // Act
        var (acquireResult, lockId) = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds));

        var lockUpdated = await _fixture.ProcessLockRepo.TryUpdateLockExpiration(
            lockId!.Value,
            instanceInternalId,
            ttlSeconds
        );

        var lockData = await _fixture.ProcessLockRepo.Get(lockId.Value);

        // Assert
        Assert.Equal(AcquireLockResult.Success, acquireResult);
        Assert.NotNull(lockId);
        Assert.NotEqual(Guid.Empty, lockId.Value);
        Assert.Equal(UpdateLockResult.LockExpired, lockUpdated);

        Assert.Equal(lockId, lockData!.Id);
        Assert.Equal(instanceInternalId, lockData.InstanceInternalId);
        Assert.Equal(startTime, lockData.LockedAt);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), lockData.LockedUntil);
        Assert.Equal(userId, lockData.LockedBy);

        var rowCount = await PostgresUtil.RunCountQuery(
            """
            SELECT count(*)
            FROM storage.instancelocks
            """
        );
        Assert.Equal(1, rowCount);
    }
}

public class ProcessLockFixture
{
    public IProcessLockRepository ProcessLockRepo { get; set; }

    public IInstanceRepository InstanceRepo { get; set; }

    public ProcessLockFixture()
    {
        var serviceList = ServiceUtil.GetServices([
            typeof(IProcessLockRepository),
            typeof(IInstanceRepository),
        ]);
        ProcessLockRepo = (IProcessLockRepository)
            serviceList.First(i => i.GetType() == typeof(PgProcessLockRepository));
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
    }
}
