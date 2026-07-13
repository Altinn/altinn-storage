#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class CleanupControllerUnitTests
{
    [Fact]
    public async Task CleanupInstancesForApp_ConsumesDomainPagesAndStorageFormatIdDirectly()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        Guid instanceGuid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        string uppercaseStorageId = instanceGuid.ToString().ToUpperInvariant();
        const int storageAccountNumber = 7;
        InstanceInternal instance = new()
        {
            Id = uppercaseStorageId,
            AppId = "ttd/app",
            Org = "ttd",
            InstanceOwner = new() { PartyId = "1337" },
            Data = [],
        };
        Queue<InstanceQueryResult> pages = new([
            new InstanceQueryResult { Instances = [instance], ContinuationToken = "next-page" },
            new InstanceQueryResult { Instances = [] },
        ]);
        List<InstanceQueryParameters> capturedParameters = [];
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        Mock<IApplicationRepository> applicationRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IDataRepository> dataRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    cancellationTokenSource.Token
                )
            )
            .Callback<InstanceQueryParameters, CancellationToken>(
                (parameters, _) => capturedParameters.Add(parameters)
            )
            .ReturnsAsync(() => pages.Dequeue());
        instanceRepositoryMock
            .Setup(repository => repository.Delete(instanceGuid, cancellationTokenSource.Token))
            .ReturnsAsync(true);
        applicationRepositoryMock
            .Setup(repository => repository.FindOne(instance.AppId, instance.Org, default))
            .ReturnsAsync(
                new Application
                {
                    Id = instance.AppId,
                    Org = instance.Org,
                    StorageAccountNumber = storageAccountNumber,
                }
            );
        blobRepositoryMock
            .Setup(repository => repository.DeleteDataBlobs(instance, storageAccountNumber))
            .ReturnsAsync(true);
        dataRepositoryMock
            .Setup(repository => repository.DeleteForInstance(uppercaseStorageId, default))
            .ReturnsAsync(true);
        CleanupController controller = new(
            instanceRepositoryMock.Object,
            applicationRepositoryMock.Object,
            blobRepositoryMock.Object,
            dataRepositoryMock.Object,
            Mock.Of<IInstanceEventRepository>(),
            NullLogger<CleanupController>.Instance
        );

        ActionResult result = await controller.CleanupInstancesForApp(
            "ttd",
            "app",
            cancellationTokenSource.Token
        );

        Assert.IsType<OkResult>(result);
        Assert.Equal(2, capturedParameters.Count);
        Assert.All(capturedParameters, parameters => Assert.Equal("ttd/app", parameters.AppId));
        Assert.All(capturedParameters, parameters => Assert.Equal(5000, parameters.Size));
        Assert.All(capturedParameters, parameters => Assert.True(parameters.IncludeDataElements));
        Assert.Null(capturedParameters[0].ContinuationToken);
        Assert.Equal("next-page", capturedParameters[1].ContinuationToken);
        Assert.Empty(pages);
        instanceRepositoryMock.VerifyAll();
        applicationRepositoryMock.VerifyAll();
        blobRepositoryMock.VerifyAll();
        dataRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task CleanupInstancesForApp_CancelledDomainResult_PreservesSuccessfulStatusAndNoSideEffects()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.Is<InstanceQueryParameters>(parameters =>
                        parameters.AppId == "ttd/app"
                        && parameters.Size == 5000
                        && parameters.ContinuationToken == null
                        && parameters.IncludeDataElements
                    ),
                    cancellationTokenSource.Token
                )
            )
            .ReturnsAsync(
                new InstanceQueryResult { Instances = [], Exception = "The query was canceled." }
            );
        CleanupController controller = new(
            instanceRepositoryMock.Object,
            Mock.Of<IApplicationRepository>(),
            Mock.Of<IBlobRepository>(),
            Mock.Of<IDataRepository>(),
            Mock.Of<IInstanceEventRepository>(),
            NullLogger<CleanupController>.Instance
        );

        ActionResult result = await controller.CleanupInstancesForApp(
            "ttd",
            "app",
            cancellationTokenSource.Token
        );

        Assert.IsType<OkResult>(result);
        instanceRepositoryMock.VerifyAll();
        instanceRepositoryMock.Verify(
            repository => repository.Delete(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
