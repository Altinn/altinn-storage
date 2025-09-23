using System;
using Altinn.Platform.Storage.Interface.Enums;
using Wolverine.Attributes;

namespace Altinn.Platform.Storage.Messages;

/// <summary>
/// Represents a message about update for an instance to send to service bus.
/// </summary>
[MessageIdentity("Altinn.DialogportenAdapter.SyncInstanceCommand")]
public record SyncInstanceToDialogportenCommand(
    string AppId, // eks: krt/krt-1012a-1
    string PartyId, // eks: 51701090
    string InstanceId, // eks: 0dbc1da6-f744-4fff-83bc-131e7988a1bb
    DateTime InstanceCreatedAt, // eks: 2025-06-18T08:54:53.6233769Z
    bool IsMigration, // Always false when Storage is sender
    InstanceEventType EventType = InstanceEventType.None); 
