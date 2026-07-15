#nullable disable

using System;
using System.Linq;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Provides string helper extension methods.
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// Removes all newline characters from the specified string.
    /// </summary>
    /// <param name="value">The string from which to remove newline characters.</param>
    /// <returns>A string with all newline characters removed, or the original string if it is null or empty.</returns>
    public static string RemoveNewlines(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("\n", string.Empty)
            ?.Replace("\r", string.Empty)
            ?.Replace(Environment.NewLine, string.Empty);
    }

    /// <summary>
    /// Cleans a string so it is safe to use in logs, mitigating log injection/forging
    /// by removing newline and other control characters from user-controlled input.
    /// </summary>
    /// <param name="value">The string to clean.</param>
    /// <returns>A cleaned string, or the original string if it is null or empty.</returns>
    public static string Clean(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return new string(value
            .RemoveNewlines()
            .Where(c => !char.IsControl(c))
            .ToArray());
    }
}
