namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Action identifiers used when authorizing operations on an instance.
/// </summary>
/// <remarks>
/// The values are part of the contract with the Altinn authorization (XACML) policies and must
/// match the action ids defined there — they cannot be renamed freely. This does not cover custom,
/// app-defined process actions (for example "pay" or "confirm"), which remain plain strings.
/// </remarks>
public static class AuthorizationActions
{
    /// <summary>Read an instance or its data.</summary>
    public const string Read = "read";

    /// <summary>Write to an instance or its data.</summary>
    public const string Write = "write";

    /// <summary>Delete an instance or its data.</summary>
    public const string Delete = "delete";

    /// <summary>Complete/confirm an instance.</summary>
    public const string Complete = "complete";

    /// <summary>Sign an instance.</summary>
    public const string Sign = "sign";

    /// <summary>Reject/abandon the current process step.</summary>
    public const string Reject = "reject";

    /// <summary>Instantiate a new instance.</summary>
    public const string Instantiate = "instantiate";
}
