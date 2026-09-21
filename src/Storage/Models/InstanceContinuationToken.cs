using System;
using System.Globalization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A keyset cursor. It identifies the last instance that a paged instance query returned.
/// </summary>
/// <param name="Timestamp">The timestamp that the query sorts by, for the last instance in the previous page.</param>
/// <param name="InternalId">The internal id of that instance. It gives the sequence when two instances have the same timestamp.</param>
public readonly record struct InstanceContinuationToken(DateTime Timestamp, long InternalId)
{
    /// <summary>
    /// Tries to read the format that <see cref="ToString"/> makes.
    /// </summary>
    /// <param name="value">The serialized token.</param>
    /// <param name="token">The parsed token, when parsing succeeds.</param>
    /// <returns><c>true</c> if the value is a correct token. If it is not, the result is <c>false</c>.</returns>
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
