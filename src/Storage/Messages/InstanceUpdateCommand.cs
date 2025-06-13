using System;

namespace Altinn.Platform.Storage.Messages;

/// <summary>
/// Represents a message about update for an instance to send to service bus.
/// </summary>
public record InstanceUpdateCommand(string InstanceId);
