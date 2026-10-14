using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class InstanceOwnerTests : IClassFixture<InstanceFixture>, IAsyncLifetime
{
    private const string _partyId = "50000000";
    private const string _otherPartyId = "50000001";

    private readonly InstanceFixture _instanceFixture;

    public InstanceOwnerTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;
    }

    public async Task InitializeAsync() => await ClearTables();

    public async Task DisposeAsync()
    {
        await PostgresUtil.UnfreezeTime();
        await ClearTables();
    }

    [Fact]
    public async Task Create_OwnerFromRegister_StoresOwnerOnceForTheParty()
    {
        // Arrange
        InstanceInternal input = NewInstance(RegisterOwner(_partyId, "25871999336"));

        // Act
        await _instanceFixture.InstanceRepo.Create(input, CancellationToken.None);

        // Assert
        string storedOwner = await PostgresUtil.RunQuery<string>(
            $"select instance -> 'InstanceOwner' ->> 'PartyId' || '|' || (instance -> 'InstanceOwner')::text from storage.instances where alternateid = '{input.Id}'"
        );
        Assert.Equal($"{_partyId}|{{\"PartyId\": \"{_partyId}\"}}", storedOwner);
        Assert.Equal(
            1,
            await PostgresUtil.RunCountQuery(
                $"select count(*) from storage.parties where partyid = {_partyId} and personnumber = '25871999336'"
            )
        );

        InstanceInternal read = await _instanceFixture.InstanceRepo.GetOne(
            input.Id,
            false,
            CancellationToken.None
        );
        AssertOwner(RegisterOwner(_partyId, "25871999336"), read.InstanceOwner);
    }

    [Fact]
    public async Task Create_OwnerWithoutPartyUuid_StoresOwnerOnTheInstance()
    {
        // Arrange
        InstanceOwner owner = new() { PartyId = _partyId, PersonNumber = "25871999336" };
        InstanceInternal input = NewInstance(owner);

        // Act
        await _instanceFixture.InstanceRepo.Create(input, CancellationToken.None);

        // Assert
        Assert.Equal(0, await PostgresUtil.RunCountQuery("select count(*) from storage.parties"));
        InstanceInternal read = await _instanceFixture.InstanceRepo.GetOne(
            input.Id,
            false,
            CancellationToken.None
        );
        AssertOwner(owner, read.InstanceOwner);
    }

    [Fact]
    public async Task UpsertInstanceOwner_ReadsOfEveryInstanceOfThePartyUseStoredOwner()
    {
        // Arrange
        InstanceOwner legacyOwner = new() { PartyId = _partyId, PersonNumber = "25871999336" };
        InstanceOwner otherOwner = new() { PartyId = _otherPartyId, PersonNumber = "02013299997" };
        InstanceInternal first = NewInstance(legacyOwner);
        InstanceInternal second = NewInstance(legacyOwner);
        InstanceInternal other = NewInstance(otherOwner);
        foreach (InstanceInternal instance in new[] { first, second, other })
        {
            await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        }

        string rowStateBefore = await GetRowState();
        InstanceOwner storedOwner = RegisterOwner(_partyId, "25871999336");

        // Act
        await _instanceFixture.InstanceRepo.UpsertInstanceOwner(
            storedOwner,
            CancellationToken.None
        );

        // Assert
        foreach (InstanceInternal instance in new[] { first, second })
        {
            InstanceInternal read = await _instanceFixture.InstanceRepo.GetOne(
                instance.Id,
                true,
                CancellationToken.None
            );
            AssertOwner(storedOwner, read.InstanceOwner);
        }

        InstanceInternal readOther = await _instanceFixture.InstanceRepo.GetOne(
            other.Id,
            false,
            CancellationToken.None
        );
        AssertOwner(otherOwner, readOther.InstanceOwner);

        InstanceQueryResult queryResult = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters { InstanceOwnerPartyId = int.Parse(_partyId), Size = 10 },
            CancellationToken.None
        );
        Assert.Equal(2, queryResult.Instances.Count);
        Assert.All(queryResult.Instances, i => AssertOwner(storedOwner, i.InstanceOwner));

        Assert.Equal(rowStateBefore, await GetRowState());
    }

    [Fact]
    public async Task Updates_ReturnInstanceWithStoredOwner()
    {
        // Arrange
        InstanceOwner owner = RegisterOwner(_partyId, "25871999336");
        InstanceInternal instance = NewInstance(owner);
        await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        instance = await _instanceFixture.InstanceRepo.GetOne(
            instance.Id,
            true,
            CancellationToken.None
        );

        // Act
        instance.Status.Substatus = new Interface.Models.Substatus { Label = "label" };
        instance.LastChanged = DateTime.UtcNow;
        InstanceInternal updated = await _instanceFixture.InstanceRepo.Update(
            instance,
            [nameof(InstanceInternal.Status), nameof(InstanceInternal.LastChanged)],
            cancellationToken: CancellationToken.None
        );
        updated.Status.ReadStatus = ReadStatus.Unread;
        InstanceInternal readStatusUpdated = await _instanceFixture.InstanceRepo.UpdateReadStatus(
            updated,
            CancellationToken.None
        );
        InstanceInternal dataValuesUpdated = await _instanceFixture.InstanceRepo.UpdateDataValues(
            instance.Id,
            new Dictionary<string, string> { ["key"] = "value" },
            cancellationToken: CancellationToken.None
        );

        // Assert
        AssertOwner(owner, updated.InstanceOwner);
        AssertOwner(owner, readStatusUpdated.InstanceOwner);
        AssertOwner(owner, dataValuesUpdated.InstanceOwner);
        Assert.Equal(
            $"{{\"PartyId\": \"{_partyId}\"}}",
            await PostgresUtil.RunQuery<string>(
                $"select (instance -> 'InstanceOwner')::text from storage.instances where alternateid = '{instance.Id}'"
            )
        );
    }

    [Fact]
    public async Task UpsertInstanceOwner_UnchangedOwner_KeepsUpdatedTimestamp()
    {
        // Arrange
        DateTimeOffset firstUpsert = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondUpsert = firstUpsert.AddDays(1);
        InstanceOwner owner = RegisterOwner(_partyId, "25871999336");
        await PostgresUtil.FreezeTime(firstUpsert);
        await _instanceFixture.InstanceRepo.UpsertInstanceOwner(owner, CancellationToken.None);
        await PostgresUtil.FreezeTime(secondUpsert);

        // Act
        await _instanceFixture.InstanceRepo.UpsertInstanceOwner(owner, CancellationToken.None);
        DateTime unchanged = await GetPartyUpdated();
        owner.PersonNumber = "02013299997";
        await _instanceFixture.InstanceRepo.UpsertInstanceOwner(owner, CancellationToken.None);
        DateTime changed = await GetPartyUpdated();

        // Assert
        Assert.Equal(firstUpsert.UtcDateTime, unchanged);
        Assert.Equal(secondUpsert.UtcDateTime, changed);
        Assert.Equal(
            "02013299997",
            await PostgresUtil.RunQuery<string>(
                $"select personnumber from storage.parties where partyid = {_partyId}"
            )
        );
    }

    [Fact]
    public async Task DeleteOrphanedInstanceOwners_DeletesOnlyOwnersWithoutInstances()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            NewInstance(RegisterOwner(_partyId, "25871999336")),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.UpsertInstanceOwner(
            RegisterOwner(_otherPartyId, "02013299997"),
            CancellationToken.None
        );

        // Act
        int deleted = await _instanceFixture.InstanceRepo.DeleteOrphanedInstanceOwners(
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, deleted);
        Assert.Equal(
            _partyId,
            await PostgresUtil.RunQuery<string>(
                "select string_agg(partyid::text, ',') from storage.parties"
            )
        );
    }

    private static InstanceOwner RegisterOwner(string partyId, string personNumber) =>
        new()
        {
            PartyId = partyId,
            PartyUuid = Guid.Parse($"00000000-0000-0000-0000-0000{partyId}"),
            PersonNumber = personNumber,
        };

    private static InstanceInternal NewInstance(InstanceOwner owner)
    {
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Id = Guid.NewGuid();
        instance.InstanceOwner = owner;
        return instance;
    }

    private static void AssertOwner(InstanceOwner expected, InstanceOwner actual)
    {
        Assert.Equal(expected.PartyId, actual.PartyId);
        Assert.Equal(expected.PartyUuid, actual.PartyUuid);
        Assert.Equal(expected.PersonNumber, actual.PersonNumber);
        Assert.Equal(expected.OrganisationNumber, actual.OrganisationNumber);
        Assert.Equal(expected.Username, actual.Username);
        Assert.Equal(expected.ExternalIdentifier, actual.ExternalIdentifier);
    }

    private static Task<string> GetRowState() =>
        PostgresUtil.RunQuery<string>(
            "select string_agg(alternateid::text || ':' || lastchanged::text || ':' || instance_version::text || ':' || md5(instance::text), ',' order by alternateid) from storage.instances"
        );

    private static Task<DateTime> GetPartyUpdated() =>
        PostgresUtil.RunQuery<DateTime>(
            $"select updated from storage.parties where partyid = {_partyId}"
        );

    private static Task<int> ClearTables() =>
        PostgresUtil.RunSql(
            "delete from storage.dataelementblobversions; delete from storage.dataelements; delete from storage.instances; delete from storage.parties;"
        );
}
