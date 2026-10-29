#nullable disable

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Constants related to authorization.
/// </summary>
public static class AuthzConstants
{
    /// <summary>
    /// Policy tag for authorizing client scope.
    /// </summary>
    public const string POLICY_SCOPE_APPDEPLOY = "ScopeAppDeploy";

    /// <summary>
    /// Policy tag for authorizing client scope.
    /// </summary>
    public const string POLICY_SCOPE_INSTANCE_READ = "ScopeInstanceRead";

    /// <summary>
    /// Policy tag for authorizing designer access
    /// </summary>
    public const string POLICY_STUDIO_DESIGNER = "StudioDesignerAccess";

    /// <summary>
    /// Policy tag for authorizing correspondence calls to SBL bridge
    /// </summary>
    public const string POLICY_CORRESPONDENCE_SBLBRIDGE = "CorrespondenceSblBridge";
}
