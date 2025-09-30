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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
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
            DateTime? created = null)
            => new(appId, partyId, instanceId, created ?? DateTime.UtcNow.AddMinutes(-5), migration, evt);

        [Fact]
        public async Task Insert_EnableSendingFalse_DoesNotInsertRow()
        {
            var cmdObj = CreateCommand(Guid.NewGuid().ToString());

            await GetRepo(new WolverineSettings() { EnableSending = false }).Insert(cmdObj, null);

            string sql = $"select count(*) from storage.outbox";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Insert_EnableSendingTrue_InsertsRow()
        {
            var cmdObj = CreateCommand(Guid.NewGuid().ToString());

            await GetRepo().Insert(cmdObj, GetConnection());

            string sql = $"select count(*) from storage.outbox";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Insert_SecondInsertWithEarlierValidFrom_UpdatesValidFrom()
        {
            var sharedId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var first = CreateCommand(sharedId, created: now, evt: InstanceEventType.Saved);
            var second = CreateCommand(sharedId, created: now, evt: InstanceEventType.Deleted);

            await GetRepo().Insert(first, GetConnection());
            await GetRepo().Insert(second, GetConnection());

            string sql = $"select count(*) from storage.outbox";
            int count = await PostgresUtil.RunCountQuery(sql);
            sql = $"select validfrom from storage.outbox";
            DateTime validfrom = await PostgresUtil.RunQuery<DateTime>(sql);
            var diff = validfrom - now;
            Assert.Equal(1, count);
            Assert.True(diff.TotalSeconds < 2); // Less than the delay given for Saved event
        }

        [Fact]
        public async Task Poll_OneWithDuplicatesAndOneSingle_Returns_2()
        {
            var sharedId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var first = CreateCommand(sharedId, created: now, evt: InstanceEventType.Created);
            var second = CreateCommand(sharedId, created: now, evt: InstanceEventType.Deleted);
            var third = CreateCommand(Guid.NewGuid().ToString(), created: now, evt: InstanceEventType.Created);

            await GetRepo().Insert(first, GetConnection());
            await GetRepo().Insert(second, GetConnection());
            await GetRepo().Insert(third, GetConnection());
            var dps = await GetRepo().Poll(10);

            Assert.Equal(2, dps.Count);
        }

        [Fact]
        public async Task Delete_RemovesRow()
        {
            var cmdObj = CreateCommand(Guid.NewGuid().ToString());

            await GetRepo().Insert(cmdObj, GetConnection());
            await GetRepo().Delete(Guid.Parse(cmdObj.InstanceId));

            string sql = $"select count(*) from storage.outbox";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task TryAcquireLeaseAsync_RespectsExistingLease()
        {
            var resource = "r1";
            var holder = Guid.NewGuid();
            var holder2 = Guid.NewGuid();

            // First acquire
            var ok1 = await GetRepo().TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(2));
            Assert.True(ok1);

            // Second acquire by different holder before expiry should fail
            var ok2 = await GetRepo().TryAcquireLeaseAsync(resource, holder2, DateTime.UtcNow.AddSeconds(2));
            Assert.False(ok2);

            // Wait until expired
            await Task.Delay(2100);
            var ok3 = await GetRepo().TryAcquireLeaseAsync(resource, holder2, DateTime.UtcNow.AddSeconds(2));
            Assert.True(ok3);
        }

        [Fact]
        public async Task RenewLeaseAsync_ExtendsLease()
        {
            var resource = "r2";
            var holder = Guid.NewGuid();

            var ok1 = await GetRepo().TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(1));
            Assert.True(ok1);

            var renewed = await GetRepo().RenewLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(3));
            Assert.True(renewed);

            // Should still block other holder
            var other = await GetRepo().TryAcquireLeaseAsync(resource, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(1));
            Assert.False(other);
        }

        [Fact]
        public async Task ReleaseLeaseAsync_RemovesLease()
        {
            var resource = "r3";
            var holder = Guid.NewGuid();

            Assert.True(await GetRepo().TryAcquireLeaseAsync(resource, holder, DateTime.UtcNow.AddSeconds(5)));

            var released = await GetRepo().ReleaseLeaseAsync(resource, holder);
            Assert.True(released);

            // Now acquisition by someone else should succeed immediately
            var ok = await GetRepo().TryAcquireLeaseAsync(resource, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(2));
            Assert.True(ok);
        }

        private static IOutboxRepository GetRepo(WolverineSettings wolverineSettings = null)
        {
            wolverineSettings ??= new WolverineSettings() { EnableSending = true };
            var serviceList = GetServices([typeof(IOutboxRepository)], wolverineSettings);
            return (IOutboxRepository)serviceList.First(i => i.GetType() == typeof(PgOutboxRepository));
        }

        private static NpgsqlConnection GetConnection()
        {
            NpgsqlDataSource dataSource = (NpgsqlDataSource)ServiceUtil.GetServices([typeof(NpgsqlDataSource)])[0]!;
            return dataSource.OpenConnection();
        }

        private static List<object> GetServices(List<Type> interfaceTypes, WolverineSettings wolverineSettings)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                .AddEnvironmentVariables();

            var config = builder.Build();

            WebApplication.CreateBuilder()
                           .Build()
                           .SetUpPostgreSql(true, config);

            IServiceCollection services = new ServiceCollection();

            services.AddLogging();
            services.AddPostgresRepositories(config);
            services.AddMemoryCache();

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
}
