using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Utils;

/// <summary>
/// Assertions on the response an action result writes, independent of the result type.
/// </summary>
internal static class ProblemAsserts
{
    private static readonly Lazy<IServiceProvider> _services = new(() =>
        new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider()
    );

    /// <summary>
    /// Executes <paramref name="result"/> and asserts that it answers with <paramref name="status"/> and a
    /// problem body as <c>application/problem+json</c>.
    /// </summary>
    /// <returns>The problem in the response body.</returns>
    public static async Task<ProblemDetails> AssertProblemAsync(IActionResult? result, int status)
    {
        Assert.NotNull(result);
        DefaultHttpContext httpContext = new() { RequestServices = _services.Value };
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteResultAsync(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor())
        );

        Assert.Equal(status, httpContext.Response.StatusCode);
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);
        httpContext.Response.Body.Position = 0;
        ProblemDetails? problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        Assert.NotNull(problem);
        Assert.Equal(status, problem.Status);
        return problem;
    }
}
