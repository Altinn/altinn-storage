#nullable disable

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TextJson = System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Interface.Enums;

/// <summary>
/// Values from EU’s controlled vocabulary Distribution status
/// https://publications.europa.eu/resource/authority/distribution-status
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
[TextJson.JsonConverter(typeof(TextJson.JsonStringEnumConverter))]
public enum DistributionStatus
{
    /// <summary>
    /// Distribution stored in an offline storage facility
    /// </summary>
    Archive,

    /// <summary>
    /// Distribution containing all information intended for publication
    /// </summary>
    Completed,

    /// <summary>
    /// Distribution that is no longer valid or recommended for use.
    /// </summary>
    Deprecated,

    /// <summary>
    /// Distribution being assembled and possibly incomplete or erroneous
    /// </summary>
    Develop,

    /// <summary>
    /// Distribution undergoing continual updating
    /// </summary>
    Ongoing,

    /// <summary>
    /// Distribution scheduled for creation or update on an established date
    /// </summary>
    Planned,

    /// <summary>
    /// Distribution needing creation or update
    /// </summary>
    Required,

    /// <summary>
    /// Distribution no longer intended for publication
    /// </summary>
    Withdrawn,
}