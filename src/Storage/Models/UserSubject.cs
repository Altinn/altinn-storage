namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A user who is not the caller. The authorization decisions are for this user.
/// </summary>
/// <param name="UserId">The user id of the user.</param>
/// <param name="AuthenticationLevel">
/// The authentication level to compare with the minimum authentication level of the policy.
/// </param>
public sealed record UserSubject(int UserId, int AuthenticationLevel);
