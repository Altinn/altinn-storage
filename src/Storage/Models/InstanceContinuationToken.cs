using System;
using System.Globalization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Keyset cursor identifying the last instance handed out by a paged instance query.
/// </summary>
/// <param name="Timestamp">The timestamp the query orders by, for the last instance in the previous page.</param>
/// <param name="InternalId">The internal id of that instance, breaking ties on the timestamp.</param>
public readonly record struct InstanceContinuationToken(DateTime Timestamp, long InternalId)
{
    /// <summary>
    /// Attempts to parse the form produced by <see cref="ToString"/>.
    /// </summary>
    /// <param name="value">The serialized token.</param>
    /// <param name="token">The parsed token, when parsing succeeds.</param>
    /// <returns><c>true</c> when the value is a well formed token; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out InstanceContinuationToken token)
    {
        token = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string[] parts = value.Split(';');
        if (
            parts.Length != 2
            || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out long ticks)
            || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out long internalId)
            || ticks < DateTime.MinValue.Ticks
            || ticks > DateTime.MaxValue.Ticks
        )
        {
            return false;
        }

        token = new InstanceContinuationToken(new DateTime(ticks, DateTimeKind.Utc), internalId);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Timestamp.Ticks};{InternalId}");
    }
}
