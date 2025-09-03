using System;
using System.Security.Cryptography;
using System.Text;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Use ASB duplicate detection + scheduled delivery to coalesce bursts per InstanceId
/// into a single message at the end of a time bucket.
/// </summary>
public static class DebounceHelper
{
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
