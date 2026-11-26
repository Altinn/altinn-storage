using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Answers 503 Service Unavailable when Altinn Authorization did not reach a decision. The request
/// is refused either way, but 403 Forbidden would tell the caller that a retry cannot help.
/// </summary>
public class AuthorizationUnavailableMiddleware(
    RequestDelegate next,
    ILogger<AuthorizationUnavailableMiddleware> logger
)
{
    /// <summary>
    /// Runs the rest of the pipeline and answers 503 for an <see cref="AuthorizationUnavailableException"/>.
    /// </summary>
    /// <param name="context">The context of the request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AuthorizationUnavailableException exception) when (!context.Response.HasStarted)
        {
            logger.LogError(
                exception,
                "// Storage PEP // No decision from Altinn Authorization. Answering 503."
            );

            await Results
                .Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    detail: exception.Message
                )
                .ExecuteAsync(context);
        }
    }
}
