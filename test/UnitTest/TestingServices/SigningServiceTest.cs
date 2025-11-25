#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;
using static Altinn.Platform.Storage.Interface.Models.SignRequest;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class SigningServiceTest
{
    public static TheoryData<Signee> SigneeData => new(
        new Signee() { UserId = "1337", PersonNumber = "22117612345", SystemUserId = null, OrganisationNumber = null },
        new Signee() { UserId = string.Empty, PersonNumber = null, SystemUserId = null, OrganisationNumber = "524446332" },
        new Signee() { UserId = string.Empty, PersonNumber = null, SystemUserId = Guid.NewGuid(), OrganisationNumber = "524446332" });

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_SignedEventDispatched(Signee signee)
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
        {
            InstanceOwner = new(),
            Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } },
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
        applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object);

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
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(Guid.NewGuid(), signRequest, performedBy, It.IsAny<CancellationToken>());

        // Assert
        Assert.True(created);
        Assert.Null(serviceError);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
        instanceEventServiceMock.VerifyAll();
    }

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task CreateSignDocument_SigningSuccessful_OldSignatureIsDeleted(Signee signee)
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        string instanceId = "123/" + instanceGuid;
        var signatureDataType = "sign-data-type";

        SignDocument oldSignDocument = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = default,
            SigneeInfo = signee,
            DataElementSignatures = []
        };

        DataElement oldSignatureDataElement = new() { DataType = signatureDataType };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        var instance = new Instance()
        {
            Id = instanceId,
            InstanceOwner = new InstanceOwner(),
            Process = new ProcessState { CurrentTask = new ProcessElementInfo { ElementId = "Task_1", AltinnTaskType = "signing" } },
            Data = [oldSignatureDataElement]
        };

        instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync((instance, 0));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true, null));

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock.Setup(
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>()))
            .ReturnsAsync((Guid.NewGuid().ToString(), null));

        dataServiceMock.Setup(x => x.DeleteImmediately(It.Is<Instance>(x => x.Id == instance.Id), It.Is<DataElement>(x => x.Id == oldSignatureDataElement.Id), It.IsAny<int?>()));

        dataServiceMock.Setup(
            dsm => dsm.UploadDataAndCreateDataElement(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<DataElement>(), 0, It.IsAny<int?>()));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();
        instanceEventServiceMock.Setup(
            esm => esm.DispatchEvent(It.Is<InstanceEventType>(ies => ies == InstanceEventType.Signed), It.IsAny<Instance>()));

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock.Setup(x => x.ReadBlob(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(oldSignDocument)));

        blobRepositoryMock.Setup(x => x.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(true);

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object);

        // Act
        var signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
                [new DataElementSignature { DataElementId = Guid.NewGuid().ToString(), Signed = true }],
            Signee = signee,
        };

        string? performedBy = !string.IsNullOrWhiteSpace(signee.UserId) ? signee.UserId : signee.OrganisationNumber;
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(Guid.NewGuid(), signRequest, performedBy, It.IsAny<CancellationToken>());

        // Assert
        Assert.True(created);
        Assert.Null(serviceError);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
        instanceEventServiceMock.VerifyAll();

        // Verify explicitly that the old signature was deleted
        dataServiceMock.Verify(x => x.DeleteImmediately(It.Is<Instance>(x => x.Id == instance.Id), It.Is<DataElement>(x => x.Id == oldSignatureDataElement.Id), It.IsAny<int?>()));
    }

    [Fact]
    public async Task CreateSignDocument_SigningFailed_InstanceNotExists()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(((Instance?)null, 0));

        var applicationServiceMock = new Mock<IApplicationService>();
        var dataServiceMock = new Mock<IDataService>();
        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object);

        // Act
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(Guid.NewGuid(), new SignRequest(), "1337", It.IsAny<CancellationToken>());

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
        instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
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
        applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application());

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock.Setup(x => x.ReadBlob(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream("whatever"u8.ToArray()));

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object);

        // Act
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(Guid.NewGuid(), new SignRequest(), "1337", It.IsAny<CancellationToken>());

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
        instanceRepositoryMock.Setup(rm => rm.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync((new Instance()
        {
            InstanceOwner = new(),
            Process = new ProcessState { CurrentTask = new ProcessElementInfo { AltinnTaskType = "CurrentTask" } }
        }, 0));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock.Setup(
                asm => asm.ValidateDataTypeForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true, null));

        var applicationRepositoryMock = new Mock<IApplicationRepository>();
        applicationRepositoryMock.Setup(am => am.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Application());

        var dataServiceMock = new Mock<IDataService>();
        dataServiceMock.Setup(
                dsm => dsm.GenerateSha256Hash(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>()))
            .ReturnsAsync((null, new ServiceError(404, "DataElement not found")));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var blobRepositoryMock = new Mock<IBlobRepository>();
        blobRepositoryMock.Setup(x => x.ReadBlob(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream("whatever"u8.ToArray()));

        var loggerMock = new Mock<ILogger<SigningService>>();

        var service = new SigningService(
            instanceRepositoryMock.Object,
            dataServiceMock.Object,
            applicationServiceMock.Object,
            instanceEventServiceMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            loggerMock.Object);

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
        (bool created, ServiceError serviceError) = await service.CreateSignDocument(Guid.NewGuid(), signRequest, performedBy, It.IsAny<CancellationToken>());

        // Assert
        Assert.False(created);
        Assert.Equal(404, serviceError.ErrorCode);
        instanceRepositoryMock.VerifyAll();
        applicationServiceMock.VerifyAll();
        dataServiceMock.VerifyAll();
    }
}
