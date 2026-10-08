using System;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// UTC timestamp representation shared by mutation JSON and PostgreSQL parameters.
/// </summary>
internal static class MutationTimestamp
{
    internal static DateTime NormalizeForPostgres(DateTime value)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return new DateTime(
            (utc.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
    }
}
