using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Utils;

/// <summary>
/// Assertions for action results that carry a <see cref="ProblemDetails"/> body.
/// </summary>
public static class ProblemDetailsAssert
{
    /// <summary>
    /// Asserts that <paramref name="result"/> is an <see cref="ObjectResult"/> with
    /// <paramref name="expectedStatus"/> and a <see cref="ProblemDetails"/> body carrying the same status.
    /// </summary>
    /// <returns>The problem details, for further assertions.</returns>
    public static ProblemDetails IsProblem(IActionResult result, int expectedStatus)
    {
        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Equal(expectedStatus, problem.Status);
        return problem;
    }
}
