#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        public static TheoryData<Signee> SigneeData => new(
            new Signee() { UserId = "1337", PersonNumber = "22117612345", SystemUserId = null, OrganisationNumber = null },
            new Signee() { UserId = string.Empty, PersonNumber = null, SystemUserId = Guid.NewGuid(), OrganisationNumber = "524446332" });

        [Theory]
        [MemberData(nameof(SigneeData))]
        public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched(Signee signee)
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
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
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>()))
                .ReturnsAsync((Guid.NewGuid().ToString(), null));
            
            dataServiceMock.Setup(
                dsm => dsm.UploadDataAndCreateDataElement(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<DataElement>(), 0, It.IsAny<int?>()));

            var instanceEventServiceMock = new Mock<IInstanceEventService>();
            instanceEventServiceMock.Setup(
                esm => esm.DispatchEvent(It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed), It.IsAny<Instance>()));

            var applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Application());

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object,
                applicationRepositoryMock.Object);

            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = signee,
            };

            // Act
            var performedBy = !string.IsNullOrWhiteSpace(signee.UserId) ? signee.UserId : signee.OrganisationNumber;
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), signRequest, performedBy, CancellationToken.None);

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
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(((Instance?)null, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            var dataServiceMock = new Mock<IDataService>();
            var instanceEventServiceMock = new Mock<IInstanceEventService>();

            var applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Application());

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object,
                applicationRepositoryMock.Object);

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), new SignRequest(), "1337", CancellationToken.None);

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
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
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

            var applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Application());

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object,
                applicationRepositoryMock.Object);

            // Act
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), new SignRequest(), "1337", CancellationToken.None);

            // Assert
            Assert.False(created);
            Assert.Equal(404, serviceError.ErrorCode);
            instanceRepositoryMock.VerifyAll();
            applicationServiceMock.VerifyAll();
        }

        [Theory]
        [MemberData(nameof(SigneeData))]
        public async Task CreateSignDocument_SigningFailed_DataElementNotExists(Signee signee)
        {
            // Arrange
            var instanceRepositoryMock = new Mock<IInstanceRepository>();
            instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
            {
                InstanceOwner = new(),
                Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } }
            }, 0));

            var applicationServiceMock = new Mock<IApplicationService>();
            applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((true, null));

            var applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Application());

            var dataServiceMock = new Mock<IDataService>();
            dataServiceMock.Setup(
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>()))
                .ReturnsAsync((null, new ServiceError(404, "DataElement not found")));

            var instanceEventServiceMock = new Mock<IInstanceEventService>();

            var service = new InstanceService(
                instanceRepositoryMock.Object, 
                dataServiceMock.Object, 
                applicationServiceMock.Object, 
                instanceEventServiceMock.Object,
                applicationRepositoryMock.Object);
            
            SignRequest signRequest = new SignRequest
            {
                SignatureDocumentDataType = "sign-data-type",
                DataElementSignatures = new List<DataElementSignature>
                {
                    new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }
                },
                Signee = signee,
            };

            // Act
            var performedBy = !string.IsNullOrWhiteSpace(signee.UserId) ? signee.UserId : signee.OrganisationNumber;
            (bool created, ServiceError serviceError) = await service.CreateSignDocument(1337, Guid.NewGuid(), signRequest, performedBy, CancellationToken.None);

            // Assert
            Assert.False(created);
            Assert.Equal(404, serviceError.ErrorCode);
            instanceRepositoryMock.VerifyAll();
            applicationServiceMock.VerifyAll();
            dataServiceMock.VerifyAll();
        }
    }
}
