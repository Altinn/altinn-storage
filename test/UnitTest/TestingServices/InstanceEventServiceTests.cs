using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using AltinnCore.Authentication.Constants;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class InstanceEventServiceTests
{
    [Fact]
    public async Task DispatchEvent()
    {
        // Arrange
        const InstanceEventType eventType = InstanceEventType.Saved;
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();
        Mock<IHttpContextAccessor> contextAccessorMock = new();

        Claim userIdClaim = new(AltinnCoreClaimTypes.UserId, "123456");
        Claim authenticationLevelClaim = new(AltinnCoreClaimTypes.AuthenticationLevel, "3");

        ClaimsIdentity identity = new();
        identity.AddClaim(userIdClaim);
        identity.AddClaim(authenticationLevelClaim);

        HttpContext context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal();
        context.User.AddIdentity(identity);

        contextAccessorMock.Setup(accessor => accessor.HttpContext).Returns(context);

        InstanceEventService target = new(
            instanceEventRepositoryMock.Object,
            contextAccessorMock.Object
        );
        Instance instance = new()
        {
            Id = "test",
            InstanceOwner = new InstanceOwner { PartyId = "someId" },
            Process = new ProcessState(),
        };
        DataElement dataElement = new() { Id = "test" };

        // Act
        await target.DispatchEvent(eventType, instance, dataElement);

        // Assert
        instanceEventRepositoryMock.Verify(
            r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>(), instance),
            Times.Once
        );
    }

    [Fact]
    public async Task DispatchEvent_ThrowsExceptionWhenNoUserIsProvided()
    {
        // Arrange
        const InstanceEventType eventType = InstanceEventType.Saved;
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();
        Mock<IHttpContextAccessor> contextAccessorMock = new();

        Claim authenticationLevelClaim = new(AltinnCoreClaimTypes.AuthenticationLevel, "3");

        ClaimsIdentity identity = new();
        identity.AddClaim(authenticationLevelClaim);

        HttpContext context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal();
        context.User.AddIdentity(identity);

        contextAccessorMock.Setup(accessor => accessor.HttpContext).Returns(context);

        InstanceEventService target = new(
            instanceEventRepositoryMock.Object,
            contextAccessorMock.Object
        );
        Instance instance = new()
        {
            Id = "test",
            InstanceOwner = new InstanceOwner { PartyId = "someId" },
            Process = new ProcessState(),
        };
        DataElement dataElement = new() { Id = "test" };

        // Act amd Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await target.DispatchEvent(eventType, instance, dataElement)
        );
        Assert.Equal(
            "Cannot dispatch event, missing a user to perform the event on behalf of",
            exception.Message
        );
    }

    [Fact]
    public async Task DispatchEvent_ThrowsExceptionWhenAuthenticationLevelIsNotProvided()
    {
        // Arrange
        const InstanceEventType eventType = InstanceEventType.Saved;
        Mock<IInstanceEventRepository> instanceEventRepositoryMock = new();
        Mock<IHttpContextAccessor> contextAccessorMock = new();

        Claim userIdClaim = new(AltinnCoreClaimTypes.UserId, "123456");

        ClaimsIdentity identity = new();
        identity.AddClaim(userIdClaim);

        HttpContext context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal();
        context.User.AddIdentity(identity);

        contextAccessorMock.Setup(accessor => accessor.HttpContext).Returns(context);

        InstanceEventService target = new(
            instanceEventRepositoryMock.Object,
            contextAccessorMock.Object
        );
        Instance instance = new()
        {
            Id = "test",
            InstanceOwner = new InstanceOwner { PartyId = "someId" },
            Process = new ProcessState(),
        };
        DataElement dataElement = new() { Id = "test" };

        // Act amd Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await target.DispatchEvent(eventType, instance, dataElement)
        );
        Assert.Equal("Cannot dispatch event without AuthenticationLevel", exception.Message);
    }
}
