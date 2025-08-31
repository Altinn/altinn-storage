using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Altinn.Platform.Storage.Messages;
using Azure.Messaging.ServiceBus;
using JasperFx.Core;
using Wolverine;
using Wolverine.AzureServiceBus;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Use ASB duplicate detection + scheduled delivery to coalesce bursts per InstanceId
/// into a single message at the end of a time bucket.
/// </summary>
public class DebounceDialogportenCommandsMapper : IAzureServiceBusEnvelopeMapper
{
    private static readonly TimeSpan _bucket = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Set a deterministic MessageId for SyncInstanceToDialogportenCommand messages, and
    /// deliver them at the end of a time bucket.
    /// </summary>
    /// <param name="envelope">Wolverine envelope</param>
    /// <param name="outgoing">Outgoing ASB message</param>
    public void MapEnvelopeToOutgoing(Envelope envelope, ServiceBusMessage outgoing)
    {
        outgoing.Body = new BinaryData(envelope.Data!);

        if (envelope.ContentType.IsNotEmpty())
        {
            outgoing.ContentType = envelope.ContentType;
        }

        if (envelope.CorrelationId.IsNotEmpty())
        {
            outgoing.CorrelationId = envelope.CorrelationId;
        }

        if (envelope.MessageType.IsNotEmpty())
        {
            outgoing.Subject = envelope.MessageType;
        }

        if (envelope.GroupId.IsNotEmpty())
        {
            outgoing.SessionId = envelope.GroupId;
        }

        if (envelope.DeliverWithin.HasValue)
        {
            outgoing.TimeToLive = envelope.DeliverWithin.Value;
        }

        foreach (var (k, v) in envelope.Headers)
        {
            outgoing.ApplicationProperties[k] = v;
        }

        if (envelope.Message is not SyncInstanceToDialogportenCommand cmd)
        {
            return;
        }

        // Compute bucket start from *now* and align the schedule to the *end* of the bucket
        var (id, bucketEndUtc) = TimeBucketId(cmd.InstanceId, _bucket, DateTimeOffset.UtcNow);

        // Keep Wolverine & ASB consistent
        envelope.Id = id;
        outgoing.MessageId = id.ToString();

        // Schedule delivery to the bucket boundary (Wolverine will use the schedule API),
        // set outgoing.ScheduledEnqueueTime for clarity
        envelope.ScheduledTime = bucketEndUtc;
        outgoing.ScheduledEnqueueTime = bucketEndUtc;
    }

    /// <summary>
    /// Default mapping of body and Id
    /// </summary>
    /// <param name="envelope">Wolverine envelop</param>
    /// <param name="incoming">Incoming message</param>
    public void MapIncomingToEnvelope(Envelope envelope, ServiceBusReceivedMessage incoming)
    {
        throw new NotImplementedException();
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
    public static (Guid Id, DateTimeOffset BucketEndUtc) TimeBucketId(string resourceId, TimeSpan bucket, DateTimeOffset nowUtc)
    {
        var utcTicks = nowUtc.UtcTicks;
        var bucketStartUtcTicks = utcTicks - (utcTicks % bucket.Ticks);
        var bucketStartUtc = new DateTimeOffset(bucketStartUtcTicks, TimeSpan.Zero);
        var bucketEndUtc = bucketStartUtc + bucket;

        var key = $"{resourceId}|{bucketStartUtc:O}|{bucket.Ticks}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return (new Guid(guidBytes), bucketEndUtc);
    }
}
