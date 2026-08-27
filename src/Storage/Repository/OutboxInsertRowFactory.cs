#nullable disable

using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Builds outbox rows for Dialogporten synchronization.
/// </summary>
/// <param name="wolverineSettings">Wolverine/outbox delivery settings.</param>
/// <param name="contextAccessor">Optional HTTP context used to disambiguate instance creation events.</param>
public sealed class OutboxInsertRowFactory(
    IOptions<WolverineSettings> wolverineSettings,
    IHttpContextAccessor contextAccessor = null
)
{
    private readonly WolverineSettings _wolverineSettings = wolverineSettings.Value;

    internal OutboxInsertRow TryBuild(SyncInstanceToDialogportenCommand command)
    {
        if (!_wolverineSettings.EnableSending)
        {
            return null;
        }

        // The created event is used both in the data controller and the instance controller. The first one gives an "instance create" event
        bool isInstanceCreate =
            command.EventType == InstanceEventType.Created
            && !(
                contextAccessor?.HttpContext?.Request.Path.Value?.EndsWith(
                    "/data",
                    StringComparison.OrdinalIgnoreCase
                ) ?? true
            );

        return new OutboxInsertRow(
            Guid.Parse(command.InstanceId),
            command.AppId,
            long.Parse(command.PartyId),
            GetEventDelaySecs(command.EventType, isInstanceCreate),
            command.InstanceCreatedAt,
            command.IsMigration,
            command.EventType
        );
    }

    private int GetEventDelaySecs(InstanceEventType eventType, bool instanceCreate) =>
        OutboxEventSyncPolicy.GetPriority(eventType, instanceCreate) switch
        {
            OutboxEventPriority.Urgent => _wolverineSettings.UrgentPriorityDelaySecs,
            OutboxEventPriority.High => _wolverineSettings.HighPriorityDelaySecs,
            OutboxEventPriority.Low => _wolverineSettings.LowPriorityDelaySecs,
            _ => _wolverineSettings.HighPriorityDelaySecs,
        };
}
