namespace Altinn.Platform.Storage.Models;

/// <summary>
/// An optional change that distinguishes an omitted value from an explicit null.
/// </summary>
/// <typeparam name="T">The value being changed.</typeparam>
public readonly record struct Change<T>
{
    private Change(T value)
    {
        IsSpecified = true;
        Value = value;
    }

    /// <summary>
    /// Gets whether the value is explicitly supplied.
    /// </summary>
    public bool IsSpecified { get; }

    /// <summary>
    /// Gets the supplied value.
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Creates a change with the supplied value, including null.
    /// </summary>
    /// <param name="value">The supplied value.</param>
    /// <returns>The specified change.</returns>
    public static Change<T> Set(T value)
    {
        return new(value);
    }
}
