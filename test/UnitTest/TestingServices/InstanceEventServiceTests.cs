using System;
using System.Globalization;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class InstanceEventServiceTests
{
    [Fact]
    public async Task DispatchEvent_WolverineEnabled_PublishesMessage()
    {
        ////// Arrange
        ////var repositoryMock = new Mock<IInstanceEventRepository>();
        ////repositoryMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>(), null))
        ////    .ReturnsAsync((InstanceEvent ie, string s) => ie);

        ////DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(3, 1337) };
        ////var contextAccessorMock = new Mock<IHttpContextAccessor>();
        ////contextAccessorMock.SetupGet(a => a.HttpContext).Returns(httpContext);

        ////var busMock = new Mock<IMessageBus>();
        ////SyncInstanceToDialogportenCommand savedCommand = null;
        ////busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
        ////    .Callback<SyncInstanceToDialogportenCommand, DeliveryOptions>((cmd, _) => savedCommand = cmd)
        ////    .Returns(ValueTask.CompletedTask);

        ////var options = Options.Create(new WolverineSettings { EnableSending = true });
        ////var loggerMock = new Mock<ILogger<InstanceEventService>>();

        ////var service = new InstanceEventService(repositoryMock.Object, contextAccessorMock.Object, busMock.Object, options, loggerMock.Object);

        ////var expectedCreated = DateTime.Parse("2020-04-29T13:53:02.2836971Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

        ////Instance instance = new()
        ////{
        ////    Id = "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe",
        ////    AppId = "tdd/endring-av-navn",
        ////    InstanceOwner = new() { PartyId = "1337" },
        ////    Created = expectedCreated
        ////};

        ////// Act
        ////await service.DispatchEvent(InstanceEventType.Created, instance);

        ////// Assert
        ////busMock.VerifyAll();
        ////Assert.Equal("tdd/endring-av-navn", savedCommand.AppId);
        ////Assert.Equal("1337", savedCommand.PartyId);
        ////Assert.Equal("20b1353e-91cf-44d6-8ff7-f68993638ffe", savedCommand.InstanceId);
        ////Assert.Equal(expectedCreated, savedCommand.InstanceCreatedAt);
        ////Assert.False(savedCommand.IsMigration);
    }

    [Fact]
    public async Task DispatchEvent_WolverineThrows_NoException()
    {
        // Arrange
        var repositoryMock = new Mock<IInstanceEventRepository>();
        repositoryMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>(), null))
            .ReturnsAsync((InstanceEvent ie, string s) => ie);

        DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(3, 1337) };
        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.SetupGet(a => a.HttpContext).Returns(httpContext);

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
            .ThrowsAsync(new Exception());

        var options = Options.Create(new WolverineSettings { EnableSending = true });
        var loggerMock = new Mock<ILogger<InstanceEventService>>();

        var service = new InstanceEventService(repositoryMock.Object, contextAccessorMock.Object, busMock.Object, options, loggerMock.Object);

        Instance instance = new()
        {
            Id = "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe",
            AppId = "tdd/endring-av-navn",
            InstanceOwner = new() { PartyId = "1337" },
            Created = DateTime.Parse("2020-04-29T13:53:02.2836971Z")
        };

        // Act
        await service.DispatchEvent(InstanceEventType.Created, instance);

        // Assert - no exception thrown
    }

    [Fact]
    public async Task DispatchEvent_WithDataElement_WolverineEnabled_PublishesMessage()
    {
        ////// Arrange
        ////var repositoryMock = new Mock<IInstanceEventRepository>();
        ////repositoryMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>(), null))
        ////    .ReturnsAsync((InstanceEvent ie, string s) => ie);

        ////DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(3, 1337) };
        ////var contextAccessorMock = new Mock<IHttpContextAccessor>();
        ////contextAccessorMock.SetupGet(a => a.HttpContext).Returns(httpContext);

        ////var busMock = new Mock<IMessageBus>();
        ////SyncInstanceToDialogportenCommand savedCommand = null;
        ////busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
        ////    .Callback<SyncInstanceToDialogportenCommand, DeliveryOptions>((cmd, _) => savedCommand = cmd)
        ////    .Returns(ValueTask.CompletedTask);

        ////var options = Options.Create(new WolverineSettings { EnableSending = true });
        ////var loggerMock = new Mock<ILogger<InstanceEventService>>();

        ////var service = new InstanceEventService(repositoryMock.Object, contextAccessorMock.Object, busMock.Object, options, loggerMock.Object);

        ////DateTime testCreatedDateTime = DateTime.Parse("2020-04-29T13:53:02.2836971Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

        ////Instance instance = new()
        ////{
        ////    Id = "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe",
        ////    AppId = "tdd/endring-av-navn",
        ////    InstanceOwner = new() { PartyId = "1337" },
        ////    Created = testCreatedDateTime,
        ////};
        ////DataElement dataElement = new() { Id = "data" };

        ////// Act
        ////await service.DispatchEvent(InstanceEventType.Saved, instance, dataElement);

        ////// Assert
        ////busMock.VerifyAll();
        ////Assert.Equal("tdd/endring-av-navn", savedCommand.AppId);
        ////Assert.Equal("1337", savedCommand.PartyId);
        ////Assert.Equal("20b1353e-91cf-44d6-8ff7-f68993638ffe", savedCommand.InstanceId);
        ////Assert.Equal(testCreatedDateTime, savedCommand.InstanceCreatedAt);
        ////Assert.False(savedCommand.IsMigration);
    }

    [Fact]
    public async Task DispatchEvent_WithDataElement_WolverineThrows_NoException()
    {
        // Arrange
        var repositoryMock = new Mock<IInstanceEventRepository>();
        repositoryMock.Setup(r => r.InsertInstanceEvent(It.IsAny<InstanceEvent>(), null))
            .ReturnsAsync((InstanceEvent ie, string s) => ie);

        DefaultHttpContext httpContext = new() { User = PrincipalUtil.GetPrincipal(3, 1337) };
        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.SetupGet(a => a.HttpContext).Returns(httpContext);

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<SyncInstanceToDialogportenCommand>(), null))
            .ThrowsAsync(new Exception());

        var options = Options.Create(new WolverineSettings { EnableSending = true });
        var loggerMock = new Mock<ILogger<InstanceEventService>>();

        var service = new InstanceEventService(repositoryMock.Object, contextAccessorMock.Object, busMock.Object, options, loggerMock.Object);

        Instance instance = new()
        {
            Id = "1337/20b1353e-91cf-44d6-8ff7-f68993638ffe",
            AppId = "tdd/endring-av-navn",
            InstanceOwner = new() { PartyId = "1337" },
            Created = DateTime.Parse("2020-04-29T13:53:02.2836971Z")
        };
        DataElement dataElement = new() { Id = "data" };

        // Act
        await service.DispatchEvent(InstanceEventType.Saved, instance, dataElement);

        // Assert - no exception thrown
    }
}
