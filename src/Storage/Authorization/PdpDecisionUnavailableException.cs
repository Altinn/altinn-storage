#nullable disable

using System;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Thrown when the policy decision point (PDP) returned no decision for an authorization request.
/// The PDP client reports transport failures and non-success responses as a missing decision rather
/// than by throwing, so this is the signal that the authorization backend was unavailable. A missing
/// decision is neither a permit nor a deny and must not be reported to the caller as either.
/// </summary>
public class PdpDecisionUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdpDecisionUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PdpDecisionUnavailableException(string message)
        : base(message) { }
}
