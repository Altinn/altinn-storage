﻿using System;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Moq;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class InstanceServiceTest
    {
        [Fact]
        public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched()
        {
            // Arrange
            var repoMock = new Mock<IInstanceRepository>();
            repoMock.Setup(rm => rm.GetOne(It.IsAny<int>(), It.IsAny<Guid>(), true)).ReturnsAsync((new Instance()
            {
                InstanceOwner = new(),
                Process = new()
            }, 0));

            var eventServiceMock = new Mock<IInstanceEventService>();
            eventServiceMock.Setup(esm => esm.DispatchEvent(It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed), It.IsAny<Instance>()));

            var service = new InstanceService(repoMock.Object, eventServiceMock.Object);

            // Act
            await service.CreateSignDocument(1337, Guid.NewGuid(), new SignRequest());

            // Assert
            eventServiceMock.VerifyAll();
        }
    }
}
