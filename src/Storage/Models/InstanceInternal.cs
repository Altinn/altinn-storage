#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Models;
using TextJson = System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Mutable instance metadata used by storage.
/// </summary>
public sealed class InstanceInternal
{
    /// <summary>
    /// Gets or sets the unique instance identifier in storage format.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the instance owner.
    /// </summary>
    public InstanceOwner InstanceOwner { get; set; }

    /// <summary>
    /// Gets or sets the application identifier.
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// Gets or sets the application owner identifier.
    /// </summary>
    public string Org { get; set; }

    /// <summary>
    /// Gets or sets the due date.
    /// </summary>
    public DateTime? DueBefore { get; set; }

    /// <summary>
    /// Gets or sets when the instance becomes visible.
    /// </summary>
    public DateTime? VisibleAfter { get; set; }

    /// <summary>
    /// Gets or sets the process state.
    /// </summary>
    public ProcessState Process { get; set; }

    /// <summary>
    /// Gets or sets the instance status.
    /// </summary>
    public InstanceStatus Status { get; set; }

    /// <summary>
    /// Gets or sets completion confirmations.
    /// </summary>
    public List<CompleteConfirmation> CompleteConfirmations { get; set; }

    /// <summary>
    /// Gets or sets data elements loaded for the instance.
    /// </summary>
    /// <remarks>
    /// Data elements are stored in their own table and hydrated separately from the instance JSON.
    /// </remarks>
    [TextJson.JsonIgnore]
    public List<DataElementInternal> Data { get; set; }

    /// <summary>
    /// Gets or sets presentation texts.
    /// </summary>
    public Dictionary<string, string> PresentationTexts { get; set; }

    /// <summary>
    /// Gets or sets instance data values.
    /// </summary>
    public Dictionary<string, string> DataValues { get; set; }

    /// <summary>
    /// Gets or sets when the instance was created.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Gets or sets who created the instance.
    /// </summary>
    public string CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets when the instance was last changed.
    /// </summary>
    public DateTime? LastChanged { get; set; }

    /// <summary>
    /// Gets or sets who last changed the instance.
    /// </summary>
    public string LastChangedBy { get; set; }

    /// <summary>
    /// Gets or sets storage-owned optimistic concurrency versions.
    /// </summary>
    [TextJson.JsonIgnore]
    public StorageVersions Versions { get; set; }

    /// <summary>
    /// Gets or sets the storage row identifier.
    /// </summary>
    /// <remarks>
    /// A value of 0 means the identifier is unset or has not been hydrated. Create leaves this value
    /// unset because the insert operation does not return the row identifier; a subsequent
    /// <c>GetOne</c> read hydrates it.
    /// </remarks>
    [TextJson.JsonIgnore]
    public long InternalId { get; set; }
}
