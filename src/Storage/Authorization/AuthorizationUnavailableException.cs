using System;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ABAC.Xacml;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Altinn Authorization did not reach a decision, so the request is neither permitted nor forbidden.
/// <see cref="AuthorizationUnavailableMiddleware"/> answers 503 Service Unavailable for it.
/// </summary>
public class AuthorizationUnavailableException : Exception
{
    private static readonly string _indeterminate = XacmlContextDecision.Indeterminate.ToString();

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationUnavailableException"/> class.
    /// </summary>
    /// <param name="response">The response from the PDP that holds no decision.</param>
    public AuthorizationUnavailableException(XacmlJsonResponse? response)
        : base(
            response?.Response is null
                ? "Altinn Authorization gave no response."
                : "Altinn Authorization answered Indeterminate."
        ) { }

    /// <summary>
    /// Whether the PDP reached a decision. It did not when it gave no response, or when it answered
    /// <c>Indeterminate</c>, which it does when it fails while it evaluates the request.
    /// </summary>
    /// <param name="response">The response from the PDP.</param>
    /// <returns><c>true</c> when every result is a decision the caller can act on.</returns>
    public static bool HasDecision([NotNullWhen(true)] XacmlJsonResponse? response)
    {
        return response?.Response is not null
            && !response.Response.Exists(result => result.Decision == _indeterminate);
    }

    /// <summary>
    /// Throws an <see cref="AuthorizationUnavailableException"/> unless the PDP reached a decision.
    /// </summary>
    /// <param name="response">The response from the PDP.</param>
    public static void ThrowIfNoDecision([NotNull] XacmlJsonResponse? response)
    {
        if (!HasDecision(response))
        {
            throw new AuthorizationUnavailableException(response);
        }
    }
}
