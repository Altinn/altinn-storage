using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
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
    public async Task GetFormSummaryAsHtml_WithBlobVersionId_PassesVersionedPathToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();
        string expectedBlobVersionId = "2024-01-15T12:00:00.0000000Z";
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _appId,
            instanceGuid.ToString(),
            expectedBlobVersionId
        );

        var (controller, blobRepoMock) = CreateController(
            instanceGuid,
            htmlDataGuid,
            xmlDataGuid,
            xmlBlobVersionId: expectedBlobVersionId
        );

        // Act
        ActionResult<Stream> result = await controller.GetFormSummaryAsHtml(
            _org,
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(result.Value);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    expectedBlobStoragePath,
                    It.IsAny<int?>(),
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
        string expectedFallbackPath = $"{_appId}/{instanceGuid}/data/{xmlDataGuid}";

        // Act
        ActionResult<Stream> result = await controller.GetFormSummaryAsHtml(
            _org,
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(result.Value);
        blobRepoMock.Verify(
            b =>
                b.ReadBlob(
                    It.IsAny<string>(),
                    expectedFallbackPath,
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetSignatureAsHtml_WithBlobVersionId_PassesVersionedPathToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid signatureDataGuid = Guid.NewGuid();
        const string expectedBlobVersionId = "signature-version-id";
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _appId,
            instanceGuid.ToString(),
            expectedBlobVersionId
        );

        var (controller, blobRepoMock) = CreateControllerWithDataElements(
            instanceGuid,
            [new DataElement { Id = signatureDataGuid.ToString(), DataType = "signature-data" }],
            "[{}]",
            expectedBlobVersionId
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetPaymentAsHtml_WithBlobVersionId_PassesVersionedPathToReadBlob()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid paymentDataGuid = Guid.NewGuid();
        const string expectedBlobVersionId = "payment-version-id";
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _appId,
            instanceGuid.ToString(),
            expectedBlobVersionId
        );

        var (controller, blobRepoMock) = CreateControllerWithDataElements(
            instanceGuid,
            [new DataElement { Id = paymentDataGuid.ToString(), DataType = "payment-data" }],
            "{}",
            expectedBlobVersionId
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
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

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
        string htmlLegacyBlobStoragePath = $"{_org}/{_app}/{instanceGuid}/data/{htmlElement.Id}";

        DataElement xmlElement = new()
        {
            Id = xmlDataGuid.ToString(),
            DataType = "xml-data",
            Metadata =
            [
                new KeyValueEntry { Key = "formid", Value = formId },
                new KeyValueEntry { Key = "lformid", Value = lformId },
            ],
        };
        string xmlLegacyBlobStoragePath = $"{_org}/{_app}/{instanceGuid}/data/{xmlElement.Id}";
        htmlElement.BlobStoragePath = htmlLegacyBlobStoragePath;
        xmlElement.BlobStoragePath = string.IsNullOrEmpty(xmlBlobVersionId)
            ? xmlLegacyBlobStoragePath
            : BlobRepository.GetVersionedBlobPath(
                _appId,
                instanceGuid.ToString(),
                xmlBlobVersionId
            );

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
            .ReturnsAsync(
                (
                    new InstanceInternal(
                        instance,
                        [
                            new DataElementInternal(htmlElement, null),
                            new DataElementInternal(xmlElement, xmlBlobVersionId),
                        ]
                    ),
                    0
                )
            );

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

    private static ContentOnDemandController CreateControllerWithMissingInstance(Guid instanceGuid)
    {
        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, 0));

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

    private static (
        ContentOnDemandController Controller,
        Mock<IBlobRepository> BlobRepoMock
    ) CreateControllerWithDataElements(
        Guid instanceGuid,
        List<DataElement> dataElements,
        string blobContent,
        string? blobVersionId
    )
    {
        List<DataElementInternal> dataElementInternals = dataElements
            .Select(dataElement =>
            {
                dataElement.BlobStoragePath = string.IsNullOrEmpty(blobVersionId)
                    ? $"{_org}/{_app}/{instanceGuid}/data/{dataElement.Id}"
                    : BlobRepository.GetVersionedBlobPath(
                        _appId,
                        instanceGuid.ToString(),
                        blobVersionId
                    );
                return new DataElementInternal(dataElement, blobVersionId);
            })
            .ToList();

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
            .ReturnsAsync((new InstanceInternal(instance, dataElementInternals), 0));

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
