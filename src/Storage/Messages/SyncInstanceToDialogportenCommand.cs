using System;
using System.ComponentModel.DataAnnotations;
using Wolverine.Attributes;

namespace Altinn.Platform.Storage.Messages;

/// <summary>
/// Represents a message about update for an instance to send to service bus.
/// </summary>
[MessageIdentity("Altinn.DialogportenAdapter.SyncInstanceCommand")]
public record SyncInstanceToDialogportenCommand(
    [Required] string AppId, // eks: krt/krt-1012a-1
    [Required] string PartyId, // eks: 51701090
    [Required] string InstanceId, // eks: 0dbc1da6-f744-4fff-83bc-131e7988a1bb
    [Required] DateTime InstanceCreatedAt, // eks: 2025-06-18T08:54:53.6233769Z
    [Required] bool IsMigration); // Always false when Storage is sender
