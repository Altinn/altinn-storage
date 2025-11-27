using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Handles the outbox repository.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PgOutboxRepository"/> class.
/// </remarks>
/// <param name="wolverineSettings">the wolverine settings</param>
/// <param name="dataSource">The npgsql data source.</param>
/// <param name="logger">The logger.</param>
/// <param name="contextAccessor">HttpContextAccessor.</param>
public class PgOutboxRepository(
    IOptions<WolverineSettings> wolverineSettings,
    NpgsqlDataSource dataSource,
    ILogger<PgOutboxRepository> logger,
    IHttpContextAccessor contextAccessor = null
) : IOutboxRepository
{
    private static readonly string _insertSql =
        @"
            insert into storage.outbox
                (  instanceid,   appid,   partyid,   validfrom,   instancecreated,   ismigration,   instanceeventtype) values
                (@_instanceid, @_appid, @_partyid, @_validfrom, @_instancecreated, @_ismigration, @_instanceeventtype)
            on conflict (instanceid) do update set validfrom = excluded.validfrom where excluded.validfrom < storage.outbox.validfrom";

    private static readonly string _deleteSql =
        "delete from storage.outbox where instanceid = @_instanceid";
    private static readonly string _pollSql =
        @"select * from storage.outbox where validfrom <= now() order by validfrom
            limit @_maxrows";

    private static readonly string _acquireLeaseSql =
        @"
            INSERT INTO storage.leases (resource, holder, expires_at)
            VALUES (@_resource, @_holder, @_expiresAt)
            ON CONFLICT (resource)
            DO UPDATE SET holder = EXCLUDED.holder, expires_at = EXCLUDED.expires_at
            WHERE leases.expires_at <= NOW()";

    private static readonly string _renewLeaseSql =
        @"
            UPDATE storage.leases SET expires_at = @_expiresAt
            WHERE resource = @_resource AND holder = @_holder AND expires_at > NOW()";

    private static readonly string _releaseLeaseSql =
        @"
            DELETE FROM storage.leases WHERE resource = @_resource AND holder = @_holder";

    private readonly ILogger<PgOutboxRepository> _logger = logger;
    private readonly WolverineSettings _wolverineSettings = wolverineSettings.Value;

    /// <inheritdoc/>
    public async Task Insert(
        SyncInstanceToDialogportenCommand dp,
        NpgsqlConnection existingConnection
    )
    {
        if (!_wolverineSettings.EnableSending)
        {
            return;
        }

        // The created event is used both in the data controller and the instance controller. The first one gives an "instance create" event
        bool isInstanceCreate =
            dp.EventType == InstanceEventType.Created
            && !(
                contextAccessor?.HttpContext?.Request.Path.Value?.EndsWith(
                    "/data",
                    StringComparison.OrdinalIgnoreCase
                ) ?? true
            );

        await using NpgsqlCommand pgcom = new(_insertSql, existingConnection);

        pgcom.Parameters.AddWithValue("_appid", NpgsqlDbType.Text, dp.AppId);
        pgcom.Parameters.AddWithValue("_instanceid", NpgsqlDbType.Uuid, Guid.Parse(dp.InstanceId));
        pgcom.Parameters.AddWithValue(
            "_validfrom",
            NpgsqlDbType.TimestampTz,
            DateTime.UtcNow.AddSeconds(GetEventDelaySecs(dp.EventType, isInstanceCreate))
        );
        pgcom.Parameters.AddWithValue(
            "_instancecreated",
            NpgsqlDbType.TimestampTz,
            dp.InstanceCreatedAt
        );
        pgcom.Parameters.AddWithValue("_ismigration", NpgsqlDbType.Boolean, dp.IsMigration);
        pgcom.Parameters.AddWithValue(
            "_instanceeventtype",
            NpgsqlDbType.Smallint,
            (int)dp.EventType
        );
        pgcom.Parameters.AddWithValue("_partyid", NpgsqlDbType.Bigint, long.Parse(dp.PartyId));

        try
        {
            await pgcom.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error inserting to outbox");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task Delete(Guid instanceId)
    {
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_deleteSql);
        pgcom.Parameters.AddWithValue("_instanceid", NpgsqlDbType.Uuid, instanceId);
        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task<List<SyncInstanceToDialogportenCommand>> Poll(int maxRows)
    {
        List<SyncInstanceToDialogportenCommand> dps = [];
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_pollSql);

        pgcom.Parameters.AddWithValue("_maxrows", NpgsqlDbType.Integer, maxRows);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            SyncInstanceToDialogportenCommand dp = new(
                await reader.GetFieldValueAsync<string>("appid"),
                (await reader.GetFieldValueAsync<long>("partyid")).ToString(),
                (await reader.GetFieldValueAsync<Guid>("instanceid")).ToString(),
                await reader.GetFieldValueAsync<DateTime>("instancecreated"),
                await reader.GetFieldValueAsync<bool>("ismigration"),
                (InstanceEventType)(await reader.GetFieldValueAsync<int>("instanceeventtype"))
            );

            dps.Add(dp);
        }

        return dps;
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireLeaseAsync(
        string resource,
        Guid holder,
        DateTime leaseExpires
    )
    {
        try
        {
            await using NpgsqlCommand pgcom = dataSource.CreateCommand(_acquireLeaseSql);
            pgcom.Parameters.AddWithValue("_resource", NpgsqlDbType.Text, resource);
            pgcom.Parameters.AddWithValue("_holder", NpgsqlDbType.Uuid, holder);
            pgcom.Parameters.AddWithValue("_expiresAt", NpgsqlDbType.TimestampTz, leaseExpires);

            return await pgcom.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error acquiring lease for outbox processing");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RenewLeaseAsync(string resource, Guid holder, DateTime leaseExpires)
    {
        try
        {
            await using NpgsqlCommand pgcom = dataSource.CreateCommand(_renewLeaseSql);
            pgcom.Parameters.AddWithValue("_resource", NpgsqlDbType.Text, resource);
            pgcom.Parameters.AddWithValue("_holder", NpgsqlDbType.Uuid, holder);
            pgcom.Parameters.AddWithValue("_expiresAt", NpgsqlDbType.TimestampTz, leaseExpires);

            return await pgcom.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error renewing lease for outbox processing");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ReleaseLeaseAsync(string resource, Guid holder)
    {
        try
        {
            await using NpgsqlCommand pgcom = dataSource.CreateCommand(_releaseLeaseSql);
            pgcom.Parameters.AddWithValue("_resource", NpgsqlDbType.Text, resource);
            pgcom.Parameters.AddWithValue("_holder", NpgsqlDbType.Uuid, holder);

            return await pgcom.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error releasing lease for outbox processing");
            return false;
        }
    }

    private int GetEventDelaySecs(InstanceEventType eventType, bool instanceCreate) =>
        eventType switch
        {
            InstanceEventType.Created => instanceCreate
                ? _wolverineSettings.UrgentPriorityDelaySecs
                : _wolverineSettings.HighPriorityDelaySecs,
            InstanceEventType.Deleted => _wolverineSettings.UrgentPriorityDelaySecs,
            InstanceEventType.Saved => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.SubstatusUpdated => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.process_StartEvent => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.process_EndEvent => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.process_StartTask => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.process_EndTask => _wolverineSettings.LowPriorityDelaySecs,
            InstanceEventType.process_AbandonTask => _wolverineSettings.LowPriorityDelaySecs,
            _ => _wolverineSettings.HighPriorityDelaySecs,
        };
}
