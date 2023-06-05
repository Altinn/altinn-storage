namespace Altinn.Platform.Storage.Interface.Enums;

/// <summary>
/// The type of relation to the connected object
/// </summary>
public enum RelationType
{
    /// <summary>
    /// The connected object is generated from the connected object and should be deleted if the reference is changed
    /// </summary>
    GeneratedFrom
}
