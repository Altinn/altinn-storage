using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class ProcessLockTests : IClassFixture<ProcessLockFixture>
{
    private readonly ProcessLockFixture _fixture;

    public ProcessLockTests(ProcessLockFixture fixture)
    {
        _fixture = fixture;

        string sql = """
            delete from storage.instancelocks;
            delete from storage.instances;
            delete from storage.dataelements;
            """;
        _ = PostgresUtil.RunSql(sql).Result;
    }

    /// <summary>
    /// Test that acquiring a lock while one is active fails, but succeeds after expiration
    /// </summary>
    [Fact]
    public async Task TryAcquireLock_WhenLockActive_ReturnsNull_WhenExpired_ReturnsNewLock()
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

        var startTime = _fixture.TimeProvider.GetUtcNow();

        // Act
        var firstLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var secondLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(30));

        var thirdLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(ttlSeconds - 30));

        var fourthLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId.Value);
        var fourthLock = await _fixture.ProcessLockRepo.Get(fourthLockId.Value);

        // Assert
        Assert.NotEqual(Guid.Empty, firstLockId);
        Assert.Null(secondLockId);
        Assert.Null(thirdLockId);
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

        var startTime = _fixture.TimeProvider.GetUtcNow();

        // Act
        var firstLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLockReleased = await _fixture.ProcessLockRepo.UpdateLockExpiration(
            firstLockId.Value,
            0
        );

        var secondLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        var secondLockReleased = await _fixture.ProcessLockRepo.UpdateLockExpiration(
            secondLockId.Value,
            0
        );

        var thirdLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId.Value);
        var secondLock = await _fixture.ProcessLockRepo.Get(secondLockId.Value);
        var thirdLock = await _fixture.ProcessLockRepo.Get(thirdLockId.Value);

        // Assert
        Assert.NotEqual(Guid.Empty, firstLockId);
        Assert.NotEqual(Guid.Empty, secondLockId);
        Assert.NotEqual(Guid.Empty, thirdLockId);

        Assert.True(firstLockReleased);
        Assert.True(secondLockReleased);

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

        var startTime = _fixture.TimeProvider.GetUtcNow();

        // Act
        var firstLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(ttlSeconds - 1));

        var firstLockUpdated = await _fixture.ProcessLockRepo.UpdateLockExpiration(
            firstLockId.Value,
            ttlSeconds
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        var secondLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        _fixture.TimeProvider.Advance(TimeSpan.FromSeconds(ttlSeconds - 2));

        var thirdLockId = await _fixture.ProcessLockRepo.TryAcquireLock(
            instanceInternalId,
            ttlSeconds,
            userId
        );

        var firstLock = await _fixture.ProcessLockRepo.Get(firstLockId.Value);
        var thirdLock = await _fixture.ProcessLockRepo.Get(thirdLockId.Value);

        // Assert
        Assert.NotEqual(Guid.Empty, firstLockId);
        Assert.Null(secondLockId);
        Assert.NotEqual(Guid.Empty, thirdLockId);

        Assert.True(firstLockUpdated);

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
    }
}

public class ProcessLockFixture
{
    public IProcessLockRepository ProcessLockRepo { get; set; }

    public IInstanceRepository InstanceRepo { get; set; }

    public FakeTimeProvider TimeProvider { get; set; }

    public ProcessLockFixture()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(
            DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        );

        var serviceList = ServiceUtil.GetServices(
            [typeof(IProcessLockRepository), typeof(IInstanceRepository)],
            configureCustomServices: services =>
            {
                services.AddSingleton<TimeProvider>(timeProvider);
            }
        );
        ProcessLockRepo = (IProcessLockRepository)
            serviceList.First(i => i.GetType() == typeof(PgProcessLockRepository));
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        TimeProvider = timeProvider;
    }
}
