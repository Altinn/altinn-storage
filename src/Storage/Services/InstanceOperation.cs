using System;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Operations that can be performed on an instance in terms of scope.
/// </summary>
public enum InstanceOperation
{
    /// <summary>
    /// Read operation
    /// </summary>
    Read,

    /// <summary>
    /// Write operation
    /// </summary>
    Write,
}

/// <summary>
/// Extensions for <see cref="InstanceOperation"/>.
/// </summary>
#pragma warning disable SA1649 // File name should match first type name
public static class InstanceOperationExtensions
#pragma warning restore SA1649 // File name should match first type name
{
    /// <summary>
    /// Converts an <see cref="InstanceOperation"/> to its corresponding scope operation string.
    /// </summary>
    /// <param name="operation">the operation</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">unknown case</exception>
    public static string ToScopeOperation(this InstanceOperation operation)
    {
        return operation switch
        {
            InstanceOperation.Read => "read",
            InstanceOperation.Write => "write",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }
}
