using Altinn.Platform.Storage.Helpers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest;

public class VersionPreconditionHelperTests
{
    [Fact]
    public void TryParse_MaximumPositiveVersion_IsAccepted()
    {
        (VersionPreconditions preconditions, ActionResult? error) =
            VersionPreconditionHelper.TryParse(int.MaxValue.ToString(), int.MaxValue.ToString());

        Assert.Null(error);
        Assert.Equal(int.MaxValue, preconditions.InstanceVersion);
        Assert.Equal(int.MaxValue, preconditions.ProcessStateVersion);
    }

    /// <summary>
    /// Model binding reports an empty header value as null, so an absent header cannot be told
    /// apart from an empty or whitespace one. All three mean "no precondition".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_AbsentOrEmptyHeaders_YieldNoPreconditions(string? value)
    {
        (VersionPreconditions preconditions, ActionResult? error) =
            VersionPreconditionHelper.TryParse(value, value);

        Assert.Null(error);
        Assert.Null(preconditions.InstanceVersion);
        Assert.Null(preconditions.ProcessStateVersion);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("not-a-version")]
    public void TryParse_MalformedVersion_RemainsBadRequest(string value)
    {
        (VersionPreconditions preconditions, ActionResult? error) =
            VersionPreconditionHelper.TryParse(value, null);

        Assert.Null(preconditions.InstanceVersion);
        Assert.Null(preconditions.ProcessStateVersion);
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(error);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("malformed_version_precondition", problem.Type);
    }
}
