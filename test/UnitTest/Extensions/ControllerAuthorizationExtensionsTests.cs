#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public class ControllerAuthorizationExtensionsTests
{
    private const int PartyId = 1337;
    private static readonly Guid _instanceGuid = Guid.NewGuid();
    private readonly Mock<IAuthorization> _authorization = new();
    private readonly ControllerBase _controller = new TestController();

    [Fact]
    public async Task GetAuthorizedInstanceOrProblem_Authorized_ReturnsInstanceWithoutProblem()
    {
        InstanceInternal expected = new() { Id = _instanceGuid };
        SetupAuthorization((expected, true));

        (InstanceInternal instance, ProblemDetails problem) =
            await _controller.GetAuthorizedInstanceOrProblem(
                _authorization.Object,
                PartyId,
                _instanceGuid,
                AuthorizationActions.Read,
                includeElements: true,
                CancellationToken.None
            );

        Assert.Same(expected, instance);
        Assert.Null(problem);
    }

    [Fact]
    public async Task GetAuthorizedInstanceOrProblem_Denied_ReturnsForbiddenProblemWithoutInstance()
    {
        SetupAuthorization((null, false));

        (InstanceInternal instance, ProblemDetails problem) =
            await _controller.GetAuthorizedInstanceOrProblem(
                _authorization.Object,
                PartyId,
                _instanceGuid,
                AuthorizationActions.Write,
                includeElements: false,
                CancellationToken.None
            );

        Assert.Null(instance);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("instance_forbidden", problem.Type);
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task GetAuthorizedInstanceOrProblem_AuthorizedButMissing_ReturnsNotFoundProblemWithIds()
    {
        SetupAuthorization((null, true));

        (InstanceInternal instance, ProblemDetails problem) =
            await _controller.GetAuthorizedInstanceOrProblem(
                _authorization.Object,
                PartyId,
                _instanceGuid,
                AuthorizationActions.Read,
                includeElements: false,
                CancellationToken.None
            );

        Assert.Null(instance);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("instance_not_found", problem.Type);
        Assert.Equal(
            $"Unable to find any instance with id: {PartyId}/{_instanceGuid}.",
            problem.Detail
        );
    }

    [Fact]
    public async Task GetAuthorizedInstanceOrProblem_WithoutIncludeElements_LoadsWithoutElements()
    {
        SetupAuthorization((new InstanceInternal(), true));

        await _controller.GetAuthorizedInstanceOrProblem(
            _authorization.Object,
            PartyId,
            _instanceGuid,
            AuthorizationActions.Delete,
            CancellationToken.None
        );

        _authorization.Verify(
            a =>
                a.GetAuthorizedInstance(
                    PartyId,
                    _instanceGuid,
                    AuthorizationActions.Delete,
                    false,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private void SetupAuthorization((InstanceInternal Instance, bool IsAuthorized) outcome)
    {
        _authorization
            .Setup(a =>
                a.GetAuthorizedInstance(
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(outcome);
    }

    private sealed class TestController : ControllerBase;
}
