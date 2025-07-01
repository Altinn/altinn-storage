using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class InstanceEventServiceTests
{
    private static Instance CreateInstance()
    {
        return new Instance
        {
            Id = "1337/00000001",
            AppId = "ttd/app",
            Created = DateTime.UtcNow,
            InstanceOwner = new InstanceOwner { PartyId = "1337" }
        };
    }

    private static InstanceEventService CreateService(Mock<IInstanceEventRepository> repoMock, Mock<IMessageBus> busMock, bool enableSending)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(c => c.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) });

        var options = new Mock<IOptions<WolverineSettings>>();
        options.SetupGet(o => o.Value).Returns(new WolverineSettings { EnableSending = enableSending });

        var logger = new Mock<ILogger<InstanceEventService>>();

        return new InstanceEventService(repoMock.Object, contextAccessor.Object, busMock.Object, options.Object, logger.Object);
    }

    [Fact]
    public async Task DispatchEvent_Enabled_PublishesMessage()
    {
        // Arrange
        var repoMock = new Mock<IInstanceEventRepository>();
        repoMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>())).ReturnsAsync(new InstanceEvent());

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), default)).Returns(Task.CompletedTask);

        var service = CreateService(repoMock, busMock, enableSending: true);
        Instance instance = CreateInstance();

        // Act
        await service.DispatchEvent(InstanceEventType.Created, instance);

        // Assert
        repoMock.Verify(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>()), Times.Once);
        busMock.Verify(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), default), Times.Once);
    }

    [Fact]
    public async Task DispatchEvent_Disabled_DoesNotPublishMessage()
    {
        // Arrange
        var repoMock = new Mock<IInstanceEventRepository>();
        repoMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>())).ReturnsAsync(new InstanceEvent());

        var busMock = new Mock<IMessageBus>();

        var service = CreateService(repoMock, busMock, enableSending: false);
        Instance instance = CreateInstance();

        // Act
        await service.DispatchEvent(InstanceEventType.Created, instance);

        // Assert
        busMock.Verify(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), default), Times.Never);
    }

    [Fact]
    public async Task DispatchEvent_PublishThrows_ExceptionIsHandled()
    {
        // Arrange
        var repoMock = new Mock<IInstanceEventRepository>();
        repoMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>())).ReturnsAsync(new InstanceEvent());

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), default)).ThrowsAsync(new Exception("fail"));

        var service = CreateService(repoMock, busMock, enableSending: true);
        Instance instance = CreateInstance();

        // Act & Assert
        await service.DispatchEvent(InstanceEventType.Created, instance);
        busMock.Verify(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), default), Times.Once);
    }
}
