#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class InstanceLockTests(InstanceLockFixture fixture)
    : IClassFixture<InstanceLockFixture>,
        IAsyncLifetime
{
    private readonly InstanceLockFixture _fixture = fixture;

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
        var (firstResult, firstLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var (secondResult, secondLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(30));

        var (thirdResult, thirdLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds));

        var (fourthResult, fourthLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.InstanceLockRepo.Get(firstLockToken!.Id);
        var fourthLock = await _fixture.InstanceLockRepo.Get(fourthLockToken!.Id);

        var firstSecretHash = SHA256.HashData(firstLockToken.Secret);
        var fourthSecretHash = SHA256.HashData(fourthLockToken.Secret);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotEqual(0, firstLockToken.Id);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, secondResult);
        Assert.Null(secondLockToken);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, thirdResult);
        Assert.Null(thirdLockToken);
        Assert.Equal(AcquireLockResult.Success, fourthResult);
        Assert.NotEqual(0, fourthLockToken.Id);

        Assert.NotNull(firstLock);
        Assert.NotNull(fourthLock);

        Assert.Equal(firstLockToken.Id, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), firstLock.LockedUntil);
        Assert.Equal(firstSecretHash, firstLock.SecretHash);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(fourthLockToken.Id, fourthLock.Id);
        Assert.Equal(instanceInternalId, fourthLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), fourthLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds), fourthLock.LockedUntil);
        Assert.Equal(fourthSecretHash, fourthLock.SecretHash);
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
        var (firstResult, firstLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLockReleased = await _fixture.InstanceLockRepo.TryUpdateLockExpiration(
            firstLockToken!,
            instanceInternalId,
            0
        );

        var (secondResult, secondLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(2));

        var secondLockReleased = await _fixture.InstanceLockRepo.TryUpdateLockExpiration(
            secondLockToken!,
            instanceInternalId,
            0
        );

        var (thirdResult, thirdLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.InstanceLockRepo.Get(firstLockToken!.Id);
        var secondLock = await _fixture.InstanceLockRepo.Get(secondLockToken!.Id);
        var thirdLock = await _fixture.InstanceLockRepo.Get(thirdLockToken!.Id);

        var firstSecretHash = SHA256.HashData(firstLockToken.Secret);
        var secondSecretHash = SHA256.HashData(secondLockToken.Secret);
        var thirdSecretHash = SHA256.HashData(thirdLockToken.Secret);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotEqual(0, firstLockToken.Id);
        Assert.NotEmpty(firstLockToken.Secret);
        Assert.Equal(AcquireLockResult.Success, secondResult);
        Assert.NotEqual(0, secondLockToken.Id);
        Assert.NotEmpty(secondLockToken.Secret);
        Assert.Equal(AcquireLockResult.Success, thirdResult);
        Assert.NotEqual(0, thirdLockToken.Id);
        Assert.NotEmpty(thirdLockToken.Secret);

        Assert.Equal(UpdateLockResult.Success, firstLockReleased);
        Assert.Equal(UpdateLockResult.Success, secondLockReleased);

        Assert.NotNull(firstLock);
        Assert.NotNull(secondLock);
        Assert.NotNull(thirdLock);

        Assert.Equal(firstLockToken.Id, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime, firstLock.LockedUntil);
        Assert.Equal(firstSecretHash, firstLock.SecretHash);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(secondLockToken.Id, secondLock.Id);
        Assert.Equal(instanceInternalId, secondLock.InstanceInternalId);
        Assert.Equal(startTime, secondLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2), secondLock.LockedUntil);
        Assert.Equal(secondSecretHash, secondLock.SecretHash);
        Assert.Equal(userId, secondLock.LockedBy);

        Assert.Equal(thirdLockToken.Id, thirdLock.Id);
        Assert.Equal(instanceInternalId, thirdLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(2), thirdLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 + ttlSeconds), thirdLock.LockedUntil);
        Assert.Equal(thirdSecretHash, thirdLock.SecretHash);
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
        var (firstResult, firstLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds - 1));

        var firstLockUpdated = await _fixture.InstanceLockRepo.TryUpdateLockExpiration(
            firstLockToken!,
            instanceInternalId,
            ttlSeconds
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds + 1));

        var (secondResult, secondLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(2 * ttlSeconds - 1));

        var (thirdResult, thirdLockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.InstanceLockRepo.Get(firstLockToken!.Id);
        var thirdLock = await _fixture.InstanceLockRepo.Get(thirdLockToken!.Id);

        var firstSecretHash = SHA256.HashData(firstLockToken.Secret);
        var thirdSecretHash = SHA256.HashData(thirdLockToken.Secret);

        // Assert
        Assert.Equal(AcquireLockResult.Success, firstResult);
        Assert.NotEqual(0, firstLockToken.Id);
        Assert.Equal(AcquireLockResult.LockAlreadyHeld, secondResult);
        Assert.Null(secondLockToken);
        Assert.Equal(AcquireLockResult.Success, thirdResult);
        Assert.NotEqual(0, thirdLockToken.Id);

        Assert.Equal(UpdateLockResult.Success, firstLockUpdated);

        Assert.NotNull(firstLock);
        Assert.NotNull(thirdLock);

        Assert.Equal(firstLockToken.Id, firstLock.Id);
        Assert.Equal(instanceInternalId, firstLock.InstanceInternalId);
        Assert.Equal(startTime, firstLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds - 1), firstLock.LockedUntil);
        Assert.Equal(firstSecretHash, firstLock.SecretHash);
        Assert.Equal(userId, firstLock.LockedBy);

        Assert.Equal(thirdLockToken.Id, thirdLock.Id);
        Assert.Equal(instanceInternalId, thirdLock.InstanceInternalId);
        Assert.Equal(startTime.AddSeconds(2 * ttlSeconds - 1), thirdLock.LockedAt);
        Assert.Equal(startTime.AddSeconds(3 * ttlSeconds - 1), thirdLock.LockedUntil);
        Assert.Equal(thirdSecretHash, thirdLock.SecretHash);
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
        var nonExistentLockId = long.MaxValue;
        var ttlSeconds = 300;
        var dummyToken = new byte[20];
        var lockToken = new LockToken(nonExistentLockId, dummyToken);

        // Act
        var result = await _fixture.InstanceLockRepo.TryUpdateLockExpiration(
            lockToken,
            instanceInternalId,
            ttlSeconds
        );

        // Assert
        Assert.Equal(UpdateLockResult.LockNotFound, result);
    }

    /// <summary>
    /// Test that Get returns null when the lock doesn't exist
    /// </summary>
    [Fact]
    public async Task Get_ReturnsNull_WhenLockDoesNotExist()
    {
        // Arrange
        var nonExistentLockId = long.MaxValue;

        // Act
        var result = await _fixture.InstanceLockRepo.Get(nonExistentLockId);

        // Assert
        Assert.Null(result);
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
        var (acquireResult, lockToken) = await _fixture.InstanceLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        await PostgresUtil.FreezeTime(startTime.AddSeconds(ttlSeconds));

        var lockUpdated = await _fixture.InstanceLockRepo.TryUpdateLockExpiration(
            lockToken!,
            instanceInternalId,
            ttlSeconds
        );

        var lockData = await _fixture.InstanceLockRepo.Get(lockToken!.Id);
        var lockSecretHash = SHA256.HashData(lockToken.Secret);

        // Assert
        Assert.Equal(AcquireLockResult.Success, acquireResult);
        Assert.NotEqual(0, lockToken.Id);
        Assert.Equal(UpdateLockResult.LockExpired, lockUpdated);

        Assert.NotNull(lockData);
        Assert.Equal(lockToken.Id, lockData.Id);
        Assert.Equal(instanceInternalId, lockData.InstanceInternalId);
        Assert.Equal(startTime, lockData.LockedAt);
        Assert.Equal(startTime.AddSeconds(ttlSeconds), lockData.LockedUntil);
        Assert.Equal(lockSecretHash, lockData.SecretHash);
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

public class InstanceLockFixture
{
    public IInstanceLockRepository InstanceLockRepo { get; set; }

    public IInstanceRepository InstanceRepo { get; set; }

    public InstanceLockFixture()
    {
        var serviceList = ServiceUtil.GetServices([
            typeof(IInstanceLockRepository),
            typeof(IInstanceRepository),
        ]);
        InstanceLockRepo = (IInstanceLockRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceLockRepository));
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
    }
}
