using System;
using Altinn.Platform.Storage.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest;

public class VersionPreconditionHelperTests
{
    [Fact]
    public void TryParse_MaximumPositiveVersion_IsAccepted()
    {
        HeaderDictionary headers = new()
        {
            [StorageHeaders.IfInstanceVersionMatch] = int.MaxValue.ToString(),
            [StorageHeaders.IfProcessStateVersionMatch] = int.MaxValue.ToString(),
        };

        (VersionPreconditions preconditions, ActionResult? error) =
            VersionPreconditionHelper.TryParse(headers);

        Assert.Null(error);
        Assert.Equal(int.MaxValue, preconditions.InstanceVersion);
        Assert.Equal(int.MaxValue, preconditions.ProcessStateVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("not-a-version")]
    public void TryParse_MalformedVersion_RemainsBadRequest(string value)
    {
        HeaderDictionary headers = new() { [StorageHeaders.IfInstanceVersionMatch] = value };

        (VersionPreconditions preconditions, ActionResult? error) =
            VersionPreconditionHelper.TryParse(headers);

        Assert.Null(preconditions.InstanceVersion);
        Assert.Null(preconditions.ProcessStateVersion);
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(error);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("malformed_version_precondition", problem.Type);
    }
}
