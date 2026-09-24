namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Storage-specific HTTP headers.
/// </summary>
public static class StorageHeaders
{
    /// <summary>
    /// Optional expected aggregate instance version request header.
    /// </summary>
    public const string IfInstanceVersionMatch = "If-Instance-Version-Match";

    /// <summary>
    /// Optional expected process-state version request header.
    /// </summary>
    public const string IfProcessStateVersionMatch = "If-Process-State-Version-Match";

    /// <summary>
    /// Current aggregate instance version response header.
    /// </summary>
    public const string InstanceVersion = "Instance-Version";

    /// <summary>
    /// Current process-state version response header.
    /// </summary>
    public const string ProcessStateVersion = "Process-State-Version";

    /// <summary>
    /// Optional idempotency key for workflow-owned aggregate saves.
    /// </summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>
    /// The user id of the person that the request is for.
    /// </summary>
    public const string UserId = "X-Ai-UserId";

    /// <summary>
    /// The authentication level that the authorization decisions use for the user in
    /// <see cref="UserId"/>.
    /// </summary>
    public const string AuthenticationLevel = "X-Ai-AuthenticationLevel";
}
