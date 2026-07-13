#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class PgA2RepositoryTests
{
    private const int ArchiveReference = 424242;
    private readonly NpgsqlDataSource _dataSource;

    public PgA2RepositoryTests()
    {
        _dataSource = (NpgsqlDataSource)
            ServiceUtil
                .GetServices([typeof(NpgsqlDataSource)])
                .Single(service => service is NpgsqlDataSource);
        _ = PostgresUtil
            .RunSql(
                "delete from storage.outbox; delete from storage.a1migrationstate; delete from storage.a2migrationstate;"
            )
            .Result;
    }

    [Fact]
    public async Task UpdateCompleteMigrationState_CommitsStateAndMigrationCreatedOutbox()
    {
        InstanceInternal instance = CreateInstance(
            "01234567-89ab-cdef-0123-456789abcdef",
            new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)
        );
        PgA2Repository repository = CreateRepository(EnabledSettings());
        await repository.CreateA2MigrationState(ArchiveReference);
        await repository.UpdateStartA2MigrationState(ArchiveReference, instance.Id);

        await repository.UpdateCompleteMigrationState(instance);

        Assert.True(
            await PostgresUtil.RunQuery<bool>(
                $"select completed is not null from storage.a2migrationstate where instanceguid = '{instance.Id}'"
            )
        );
        await AssertOutbox(
            instance,
            InstanceEventType.Created,
            new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)
        );
    }

    [Fact]
    public async Task SendDeleteToDialogporten_CommitsMigrationDeletedOutbox()
    {
        InstanceInternal instance = CreateInstance(
            "21234567-89ab-cdef-0123-456789abcdef",
            new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)
        );
        PgA2Repository repository = CreateRepository(EnabledSettings());

        await repository.SendDeleteToDialogporten(instance);

        await AssertOutbox(
            instance,
            InstanceEventType.Deleted,
            new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)
        );
    }

    private PgA2Repository CreateRepository(WolverineSettings settings) =>
        new(
            _dataSource,
            new PgOutboxRepository(
                Options.Create(settings),
                _dataSource,
                NullLogger<PgOutboxRepository>.Instance
            ),
            Options.Create(settings)
        );

    private static WolverineSettings EnabledSettings() =>
        new() { EnableSending = true, EnableA2Migration = true };

    private static InstanceInternal CreateInstance(string id, DateTime created) =>
        new()
        {
            Id = id,
            AppId = "ttd/migration-app",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
            Created = created,
        };

    private static async Task AssertOutbox(
        InstanceInternal instance,
        InstanceEventType expectedEventType,
        DateTime expectedCreated
    )
    {
        Assert.Equal(
            Guid.Parse(instance.Id),
            await PostgresUtil.RunQuery<Guid>("select instanceid from storage.outbox")
        );
        Assert.Equal(
            instance.AppId,
            await PostgresUtil.RunQuery<string>("select appid from storage.outbox")
        );
        Assert.Equal(
            long.Parse(instance.InstanceOwner.PartyId),
            await PostgresUtil.RunQuery<long>("select partyid from storage.outbox")
        );
        Assert.Equal(
            expectedCreated,
            await PostgresUtil.RunQuery<DateTime>("select instancecreated from storage.outbox")
        );
        Assert.True(await PostgresUtil.RunQuery<bool>("select ismigration from storage.outbox"));
        Assert.Equal(
            (int)expectedEventType,
            await PostgresUtil.RunQuery<int>("select instanceeventtype::int from storage.outbox")
        );
    }
}
