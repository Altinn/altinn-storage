#nullable disable

using System;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Thrown when no decision could be obtained from the policy decision point (PDP) for an authorization
/// request, either because the PDP call failed or because it returned no result. The PDP client reports
/// most transport failures and non-success responses as a missing decision rather than by throwing, so
/// both shapes signal the same thing: the authorization backend was unavailable. A missing decision is
/// neither a permit nor a deny and must not be reported to the caller as either.
/// </summary>
public class PdpDecisionUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdpDecisionUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PdpDecisionUnavailableException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdpDecisionUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The failure of the PDP call.</param>
    public PdpDecisionUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
