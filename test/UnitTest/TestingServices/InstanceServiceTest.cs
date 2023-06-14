using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Moq;

using Xunit;
using static Altinn.Platform.Storage.Interface.Models.SignRequest;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class InstanceServiceTest
    {
        [Fact]
        public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<int>(), It.IsAny<Guid>(), true)).ReturnsAsync((new Instance()
            {
                InstanceOwner = new(),
                Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } }
            }, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((true, null));

            var dataServiceMock = new Mock<IDataService>();
            dataServiceMock.Setup(
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync((Guid.NewGuid().ToString(), null));
            
            dataServiceMock.Setup(
                dsm => dsm.UploadDataAndCreateDataElement(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<DataElement>()));

            var instanceEventServiceMock = new Mock<IInstanceEventService>();
            instanceEventServiceMock.Setup(
                esm => esm.DispatchEvent(It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed), It.IsAny<Instance>()));

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object);

            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" }
            };

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), signRequest, 1337);

            // Assert
            Assert.True(created);
            Assert.Null(serviceError);
            instanceRepositoryMock.VerifyAll();
            applicationServiceMock.VerifyAll();
            dataServiceMock.VerifyAll();
            instanceEventServiceMock.VerifyAll();
        }

        [Fact]
        public async Task CreateSignDocument_SigningFailed_InstanceNotExists()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<int>(), It.IsAny<Guid>(), true)).ReturnsAsync(((Instance)null, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            var dataServiceMock = new Mock<IDataService>();
            var instanceEventServiceMock = new Mock<IInstanceEventService>();

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object);

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), new SignRequest(), 1337);

            // Assert
            Assert.False(created);
            Assert.Equal(404, serviceError.ErrorCode);
            instanceRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task CreateSignDocument_SigningFailed_InvalidDatatype()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<int>(), It.IsAny<Guid>(), true)).ReturnsAsync((new Instance()
            {
                InstanceOwner = new(),
                Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } }
            }, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((false, new ServiceError(404, $"Cannot find application in storage")));

            var dataServiceMock = new Mock<IDataService>();
            var instanceEventServiceMock = new Mock<IInstanceEventService>();

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object);

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), new SignRequest(), 1337);

            // Assert
            Assert.False(created);
            Assert.Equal(404, serviceError.ErrorCode);
            instanceRepositoryMock.VerifyAll();
            applicationServiceMock.VerifyAll();
        }

        [Fact]
        public async Task CreateSignDocument_SigningFailed_DataElementNotExists()
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<int>(), It.IsAny<Guid>(), true)).ReturnsAsync((new Instance()
            {
                InstanceOwner = new(),
                Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } }
            }, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((true, null));

            var dataServiceMock = new Mock<IDataService>();
            dataServiceMock.Setup(
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync((null, new ServiceError(404, "DataElement not found")));

            var instanceEventServiceMock = new Mock<IInstanceEventService>();

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object);
            
            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" }
            };

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), signRequest, 1337);

            // Assert
            Assert.False(created);
            Assert.Equal(404, serviceError.ErrorCode);
            instanceRepositoryMock.VerifyAll();
            applicationServiceMock.VerifyAll();
            dataServiceMock.VerifyAll();
        }
    }
}
