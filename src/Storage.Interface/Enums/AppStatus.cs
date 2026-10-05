#nullable disable

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TextJson = System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Interface.Enums;

/// <summary>
/// Values from EU’s controlled vocabulary public service status
/// http://purl.org/adms/status/
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
[TextJson.JsonConverter(typeof(TextJson.JsonStringEnumConverter))]
public enum AppStatus
{
    /// <summary>
    /// The public service is fully active, developed, and available for use.
    /// </summary>
    Completed,

    /// <summary>
    /// The service profile or metadata structure is marked as obsolete, usually in favor of a newer variation
    /// </summary>
    Deprecated,

    /// <summary>
    /// The service is currently being planned, designed, or undergoing technical implementation.
    /// </summary>
    UnderDevelopment,

    /// <summary>
    /// The public service has been retired or is no longer provided by the competent public organization.
    /// </summary>
    Withdrawn,
}