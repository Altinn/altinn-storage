using Altinn.Platform.Storage.Interface.Enums;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Delivery policy for an aggregate mutation whose routing metadata is read under the instance lock.
/// </summary>
internal sealed record InstanceMutationOutboxDelivery(
    int DelaySeconds,
    InstanceEventType EventType
);
