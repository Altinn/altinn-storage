#nullable disable

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Maps <see cref="PdpDecisionUnavailableException"/> to a 503 Service Unavailable response so that an
/// unavailable authorization backend is reported as such by every endpoint, instead of surfacing as
/// the generic 500 produced by the exception handler.
/// </summary>
/// <param name="logger">The logger.</param>
public class PdpDecisionUnavailableExceptionFilter(
    ILogger<PdpDecisionUnavailableExceptionFilter> logger
) : IExceptionFilter
{
    /// <inheritdoc/>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PdpDecisionUnavailableException exception)
        {
            return;
        }

        logger.LogError(
            exception,
            "No authorization decision could be obtained from the PDP in {Action}",
            context.ActionDescriptor.DisplayName
        );

        context.Result = new ObjectResult(
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Authorization decision unavailable",
                Detail =
                    "No decision could be obtained from the authorization service. Try again later.",
            }
        )
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}
