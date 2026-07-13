using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class ContentOnDemandControllerUnitTests
{
    private const string _org = "ttd";
    private const string _app = "apps-test";

    [Fact]
    public async Task GetSignatureAsHtml_MissingInstance_ReturnsNotFound()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        ContentOnDemandController controller = CreateControllerWithMissingInstance(instanceGuid);

        // Act
        ActionResult result = await controller.GetSignatureAsHtml(
            _org,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPaymentAsHtml_MissingInstance_ReturnsNotFound()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        ContentOnDemandController controller = CreateControllerWithMissingInstance(instanceGuid);

        // Act
        ActionResult result = await controller.GetPaymentAsHtml(
            _org,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetFormSummaryAsHtml_MissingInstance_ReturnsNotFound()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        ContentOnDemandController controller = CreateControllerWithMissingInstance(instanceGuid);

        // Act
        ActionResult<Stream> result = await controller.GetFormSummaryAsHtml(
            _org,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetFormdataAsPdf_MissingInstance_ReturnsNotFound()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        ContentOnDemandController controller = CreateControllerWithMissingInstance(instanceGuid);

        // Act
        ActionResult<Stream> result = await controller.GetFormdataAsPdf(
            _org,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetFormdataAsHtml_MissingInstance_ReturnsNotFound()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        ContentOnDemandController controller = CreateControllerWithMissingInstance(instanceGuid);

        // Act
        ActionResult<Stream> result = await controller.GetFormdataAsHtml(
            _org,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static ContentOnDemandController CreateControllerWithMissingInstance(Guid instanceGuid)
    {
        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        Mock<IBlobRepository> blobRepoMock = new();
        Mock<IA2Repository> a2RepoMock = new();
        Mock<IApplicationRepository> appRepoMock = new();
        Mock<IA2OndemandFormattingService> formattingMock = new();
        Mock<IPdfGeneratorClient> pdfMock = new();
        IOptions<GeneralSettings> settings = Options.Create(new GeneralSettings());

        return new ContentOnDemandController(
            instanceRepoMock.Object,
            blobRepoMock.Object,
            a2RepoMock.Object,
            appRepoMock.Object,
            settings,
            formattingMock.Object,
            pdfMock.Object
        );
    }
}
