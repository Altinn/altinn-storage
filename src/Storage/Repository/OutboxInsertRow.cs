#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Enums;

namespace Altinn.Platform.Storage.Repository;

internal sealed record OutboxInsertRow(
    Guid InstanceId,
    string AppId,
    long PartyId,
    int DelaySeconds,
    DateTime InstanceCreated,
    bool IsMigration,
    InstanceEventType InstanceEventType
);
