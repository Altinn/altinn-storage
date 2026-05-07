using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    private const string _appId = "ttd/apps-test";

    [Fact]
    public async Task GetFormSummaryAsHtml_WithBlobVersionId_PassesVersionIdToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();
        string expectedBlobVersionId = "2024-01-15T12:00:00.0000000Z";

        var (controller, blobRepoMock) = CreateController(
            instanceGuid,
            htmlDataGuid,
            xmlDataGuid,
            xmlBlobVersionId: expectedBlobVersionId
        );

        // Act
        Stream result = await controller.GetFormSummaryAsHtml(
            _org,
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(result);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    It.Is<string>(p => p.Contains(xmlDataGuid.ToString())),
                    It.IsAny<int?>(),
                    expectedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetFormSummaryAsHtml_WithoutBlobVersionId_FallsBackToCurrentBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();

        var (controller, blobRepoMock) = CreateController(
            instanceGuid,
            htmlDataGuid,
            xmlDataGuid,
            xmlBlobVersionId: null
        );

        // Act
        Stream result = await controller.GetFormSummaryAsHtml(
            _org,
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(result);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    It.Is<string>(p => p.Contains(xmlDataGuid.ToString())),
                    It.IsAny<int?>(),
                    null,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetSignatureAsHtml_WithBlobVersionId_PassesVersionIdToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid signatureDataGuid = Guid.NewGuid();
        const string expectedBlobVersionId = "signature-version-id";
        string expectedBlobStoragePath = $"{_org}/{_app}/{instanceGuid}/data/{signatureDataGuid}";

        var (controller, blobRepoMock) = CreateControllerWithDataElements(
            instanceGuid,
            [
                new DataElement
                {
                    Id = signatureDataGuid.ToString(),
                    DataType = "signature-data",
                    BlobVersionId = expectedBlobVersionId,
                },
            ],
            "[{}]"
        );

        // Act
        ActionResult result = await controller.GetSignatureAsHtml(
            _org,
            _app,
            instanceGuid,
            signatureDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<ViewResult>(result);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    It.IsAny<int?>(),
                    expectedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetPaymentAsHtml_WithBlobVersionId_PassesVersionIdToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid paymentDataGuid = Guid.NewGuid();
        const string expectedBlobVersionId = "payment-version-id";
        string expectedBlobStoragePath = $"{_org}/{_app}/{instanceGuid}/data/{paymentDataGuid}";

        var (controller, blobRepoMock) = CreateControllerWithDataElements(
            instanceGuid,
            [
                new DataElement
                {
                    Id = paymentDataGuid.ToString(),
                    DataType = "payment-data",
                    BlobVersionId = expectedBlobVersionId,
                },
            ],
            "{}"
        );

        // Act
        ActionResult result = await controller.GetPaymentAsHtml(
            _org,
            _app,
            instanceGuid,
            paymentDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.IsType<ViewResult>(result);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    It.IsAny<int?>(),
                    expectedBlobVersionId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private static (
        ContentOnDemandController Controller,
        Mock<IBlobRepository> BlobRepoMock
    ) CreateController(
        Guid instanceGuid,
        Guid htmlDataGuid,
        Guid xmlDataGuid,
        string? xmlBlobVersionId
    )
    {
        string formId = "1234";
        string lformId = "5678";

        DataElement htmlElement = new()
        {
            Id = htmlDataGuid.ToString(),
            DataType = "html-data",
            Metadata = [new KeyValueEntry { Key = "formid", Value = formId }],
        };

        DataElement xmlElement = new()
        {
            Id = xmlDataGuid.ToString(),
            DataType = "xml-data",
            BlobVersionId = xmlBlobVersionId,
            Metadata =
            [
                new KeyValueEntry { Key = "formid", Value = formId },
                new KeyValueEntry { Key = "lformid", Value = lformId },
            ],
        };

        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = [htmlElement, xmlElement],
        };

        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 0));

        Mock<IApplicationRepository> appRepoMock = new();
        appRepoMock
            .Setup(r => r.FindOne(_appId, _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = _appId, Org = _org });

        Mock<IA2Repository> a2RepoMock = new();
        a2RepoMock
            .Setup(r =>
                r.GetXsls(_org, _app, int.Parse(lformId), It.IsAny<string>(), It.IsAny<int>())
            )
            .ReturnsAsync(new List<(string Xsl, bool IsPortrait)> { ("<xsl/>", true) });

        Mock<IBlobRepository> blobRepoMock = new();
        blobRepoMock
            .Setup(r =>
                r.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("<xml/>")));

        Mock<IA2OndemandFormattingService> formattingMock = new();
        formattingMock
            .Setup(f => f.GetFormdataHtml(It.IsAny<PrintViewXslBEList>(), It.IsAny<Stream>()))
            .Returns("<html>test</html>");

        Mock<IPdfGeneratorClient> pdfMock = new();
        IOptions<GeneralSettings> settings = Options.Create(new GeneralSettings());

        var controller = new ContentOnDemandController(
            instanceRepoMock.Object,
            blobRepoMock.Object,
            a2RepoMock.Object,
            appRepoMock.Object,
            settings,
            formattingMock.Object,
            pdfMock.Object
        );

        return (controller, blobRepoMock);
    }

    private static (
        ContentOnDemandController Controller,
        Mock<IBlobRepository> BlobRepoMock
    ) CreateControllerWithDataElements(
        Guid instanceGuid,
        List<DataElement> dataElements,
        string blobContent
    )
    {
        Instance instance = new()
        {
            Id = $"555/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = dataElements,
        };

        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 0));

        Mock<IApplicationRepository> appRepoMock = new();
        appRepoMock
            .Setup(r => r.FindOne(_appId, _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = _appId, Org = _org });

        Mock<IA2Repository> a2RepoMock = new();
        Mock<IBlobRepository> blobRepoMock = new();
        blobRepoMock
            .Setup(r =>
                r.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(blobContent)));

        Mock<IA2OndemandFormattingService> formattingMock = new();
        Mock<IPdfGeneratorClient> pdfMock = new();
        IOptions<GeneralSettings> settings = Options.Create(new GeneralSettings());

        var controller = new ContentOnDemandController(
            instanceRepoMock.Object,
            blobRepoMock.Object,
            a2RepoMock.Object,
            appRepoMock.Object,
            settings,
            formattingMock.Object,
            pdfMock.Object
        );

        return (controller, blobRepoMock);
    }
}
