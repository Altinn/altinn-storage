using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Altinn.Platform.Storage.Messages;
using Azure.Messaging.ServiceBus;
using Wolverine;
using Wolverine.AzureServiceBus;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Overrides the default mapper to utilize ASB deduplication based on instance id and a time bucket.
/// </summary>
public class DeduplicationAzureServiceBusMapper : IAzureServiceBusEnvelopeMapper
{
    /// <summary>
    /// Overrides the default mapper to utilize ASB deduplication based on instance id and a time bucket.
    /// </summary>
    /// <param name="envelope">The Wolverine envelope</param>
    /// <param name="outgoing">The ASB message</param>
    public void MapEnvelopeToOutgoing(Envelope envelope, ServiceBusMessage outgoing)
    {
        outgoing.Body = new BinaryData(envelope.Data);
        if (envelope.Message is SyncInstanceToDialogportenCommand command)
        {
            outgoing.MessageId = GetTimebucketedGuid(command.InstanceId, TimeSpan.FromSeconds(3), DateTimeOffset.UtcNow).ToString();
        }
    }

    /// <summary>
    /// Default mapping from ASB message to Wolverine envelope
    /// </summary>
    /// <param name="envelope">2</param>
    /// <param name="incoming">4</param>
    public void MapIncomingToEnvelope(Envelope envelope, ServiceBusReceivedMessage incoming)
    {
        envelope.Data = incoming.Body.ToArray();
        if (Guid.TryParse(incoming.MessageId, out var g))
        {
            envelope.Id = g;
        }
    }

    /// <summary>
    /// Default mapping from Wolverine envelope to ASB message headers
    /// </summary>
    /// <returns></returns>
    public IEnumerable<string> AllHeaders()
    {
        yield break;
    }

    /// <summary>
    /// Returns a timebucketed GUID based on the resourceId, bucket size and current time.
    /// </summary>
    /// <param name="resourceId">The id on which the the GUID shoule be based</param>
    /// <param name="bucket">Time bucket to use (default 1 second)</param>
    /// <param name="nowUtc">Current time</param>
    /// <returns></returns>
    private static Guid GetTimebucketedGuid(string resourceId, TimeSpan bucket, DateTimeOffset nowUtc)
    {
        var ticksPerBucket = bucket.Ticks <= 0 ? TimeSpan.FromSeconds(1).Ticks : bucket.Ticks;
        var bucketStart = new DateTimeOffset(nowUtc.Ticks - (nowUtc.Ticks % ticksPerBucket), TimeSpan.Zero);

        var key = $"{resourceId}|{bucketStart:O}|{bucket.Ticks}";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
