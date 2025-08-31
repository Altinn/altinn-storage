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
/// Use ASB duplicate detection + scheduled delivery to coalesce bursts per InstanceId
/// into a single message at the end of a time bucket.
/// </summary>
public class DeduplicationAzureServiceBusMapper : IAzureServiceBusEnvelopeMapper
{
    private static readonly TimeSpan Bucket = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Set a deterministic MessageId for SyncInstanceToDialogportenCommand messages, and
    /// deliver them at the end of a time bucket.
    /// </summary>
    /// <param name="envelope">Wolverine envelope</param>
    /// <param name="outgoing">Outgoing ASB message</param>
    public void MapEnvelopeToOutgoing(Envelope envelope, ServiceBusMessage outgoing)
    {
        // Keep Wolverine's serialized body
        outgoing.Body = new BinaryData(envelope.Data);

        if (envelope.Message is SyncInstanceToDialogportenCommand cmd)
        {
            // Compute bucket start from *now* and align the schedule to the *end* of the bucket
            var (id, bucketEndUtc) = TimeBucketId(cmd.InstanceId, Bucket, DateTimeOffset.UtcNow);

            // Keep Wolverine & ASB consistent
            envelope.Id = id;
            outgoing.MessageId = id.ToString();

            // Schedule delivery to the bucket boundary (Wolverine will use the schedule API),
            // set outgoing.ScheduledEnqueueTime for clarity
            envelope.ScheduledTime = bucketEndUtc;
            outgoing.ScheduledEnqueueTime = bucketEndUtc;
        }
    }

    /// <summary>
    /// Default mapping of body and Id
    /// </summary>
    /// <param name="envelope">Wolverine envelop</param>
    /// <param name="incoming">Incoming message</param>
    public void MapIncomingToEnvelope(Envelope envelope, ServiceBusReceivedMessage incoming)
    {
        envelope.Data = incoming.Body.ToArray();
        if (Guid.TryParse(incoming.MessageId, out var g))
        {
            envelope.Id = g;
        }
    }

    /// <summary>
    /// Default headers - none needed
    /// </summary>
    /// <returns></returns>
    public IEnumerable<string> AllHeaders()
    {
        yield break;
    }

    /// <summary>
    /// Build a deterministic Guid from (resourceId, bucketStart), and return the bucket end time.
    /// </summary>
    private static (Guid Id, DateTimeOffset BucketEndUtc) TimeBucketId(string resourceId, TimeSpan bucket, DateTimeOffset nowUtc)
    {
        var ticksPerBucket = bucket <= TimeSpan.Zero ? TimeSpan.FromSeconds(1).Ticks : bucket.Ticks;
        var bucketStartUtc = new DateTimeOffset(nowUtc.Ticks - (nowUtc.Ticks % ticksPerBucket), TimeSpan.Zero);
        var bucketEndUtc = bucketStartUtc + TimeSpan.FromTicks(ticksPerBucket);

        var key = $"{resourceId}|{bucketStartUtc:O}|{ticksPerBucket}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return (new Guid(guidBytes), bucketEndUtc);
    }
}
