using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Parses version precondition headers and writes current version response headers.
/// </summary>
public static class VersionPreconditionHelper
{
    /// <summary>
    /// Parses both optional version precondition headers. An absent header is a null, empty, or
    /// whitespace value: model binding reports an empty header value as null, so the three cannot
    /// be told apart and all mean "no precondition".
    /// </summary>
    public static (VersionPreconditions Preconditions, ActionResult? Error) TryParse(
        string? ifInstanceVersionMatch,
        string? ifProcessStateVersionMatch
    )
    {
        (int? expectedInstanceVersion, ActionResult? instanceError) = TryParseHeader(
            ifInstanceVersionMatch,
            StorageHeaders.IfInstanceVersionMatch
        );
        if (instanceError is not null)
        {
            return (VersionPreconditions.None, instanceError);
        }

        (int? expectedProcessStateVersion, ActionResult? processError) = TryParseHeader(
            ifProcessStateVersionMatch,
            StorageHeaders.IfProcessStateVersionMatch
        );
        if (processError is not null)
        {
            return (VersionPreconditions.None, processError);
        }

        return (
            new VersionPreconditions(expectedInstanceVersion, expectedProcessStateVersion),
            null
        );
    }

    /// <summary>
    /// Writes current version response headers.
    /// </summary>
    public static void WriteVersionResponseHeaders(
        HttpResponse response,
        int instanceVersion,
        int processStateVersion
    )
    {
        response.Headers[StorageHeaders.InstanceVersion] = instanceVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        response.Headers[StorageHeaders.ProcessStateVersion] = processStateVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    /// <summary>
    /// Writes current version response headers from storage-owned versions.
    /// </summary>
    public static void WriteVersionResponseHeaders(HttpResponse response, StorageVersions versions)
    {
        WriteVersionResponseHeaders(
            response,
            versions.InstanceVersion,
            versions.ProcessStateVersion
        );
    }

    /// <summary>
    /// Writes current version response headers from an internal instance.
    /// </summary>
    public static void WriteVersionResponseHeaders(HttpResponse response, InstanceInternal instance)
    {
        WriteVersionResponseHeaders(response, instance.Versions);
    }

    /// <summary>
    /// Creates a 412 response for a version mismatch and writes current version headers.
    /// </summary>
    public static ObjectResult VersionMismatch(
        HttpResponse response,
        StorageVersionMismatchException exception
    )
    {
        WriteVersionResponseHeaders(
            response,
            exception.CurrentInstanceVersion,
            exception.CurrentProcessStateVersion
        );

        string code = exception switch
        {
            InstanceVersionMismatchException => "instance_version_mismatch",
            ProcessStateVersionMismatchException => "process_state_version_mismatch",
            _ => "version_mismatch",
        };

        return new ObjectResult(
            new ProblemDetails
            {
                Status = StatusCodes.Status412PreconditionFailed,
                Type = code,
                Title = exception.Message,
            }
        )
        {
            StatusCode = StatusCodes.Status412PreconditionFailed,
        };
    }

    private static (int? Value, ActionResult? Error) TryParseHeader(
        string? value,
        string headerName
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        if (
            !int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed
            )
            || parsed <= 0
        )
        {
            return (null, MalformedVersionPreconditionProblem(headerName));
        }

        return (parsed, null);
    }

    private static BadRequestObjectResult MalformedVersionPreconditionProblem(string headerName) =>
        new(
            new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "malformed_version_precondition",
                Title = $"{headerName} must be a positive 32-bit integer.",
            }
        );
}

/// <summary>
/// Optional version preconditions from request headers.
/// </summary>
public sealed record VersionPreconditions(int? InstanceVersion, int? ProcessStateVersion)
{
    /// <summary>
    /// Empty version preconditions.
    /// </summary>
    public static VersionPreconditions None { get; } = new(null, null);
}
