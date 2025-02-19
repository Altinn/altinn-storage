using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Helper class for telemetry configuration.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class TelemetryHelpers
{
    /// <summary>
    /// Checks if the given URL should be excluded from telemetry.
    /// </summary>
    /// <param name="url">The uri to check for excluded paths</param>
    /// <returns>true if the path should be excluded, otherwise false</returns>
    public static bool ShouldExclude(Uri url)
        => ShouldExclude(url.LocalPath.AsSpan());

    /// <summary>
    /// Checks if the given path should be excluded from telemetry.
    /// </summary>
    /// <param name="localPath">The path string to check for excluded paths</param>
    /// <returns>true if the path should be excluded, otherwise false</returns>
    public static bool ShouldExclude(PathString localPath)
        => ShouldExclude(localPath.HasValue ? localPath.Value.AsSpan() : []);

    private static bool ShouldExclude(ReadOnlySpan<char> localPath)
    {
        while (localPath.Length > 0 && localPath[^1] == '/')
        {
            localPath = localPath[..^1];
        }

        if (localPath.EndsWith("/health", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
