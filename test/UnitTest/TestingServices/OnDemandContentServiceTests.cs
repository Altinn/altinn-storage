using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class OnDemandContentServiceTests
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
        string expectedBlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _appId,
            instanceGuid,
            expectedBlobVersionId
        );

        var (service, blobRepoMock) = CreateService(
            instanceGuid,
            CreateFormSummaryDataElements(htmlDataGuid, xmlDataGuid, expectedBlobVersionId),
            "<xml/>"
        );

        // Act
        Stream result = await service.GetFormSummaryAsHtml(
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
        string expectedFallbackPath = $"{_org}/{_app}/{instanceGuid}/data/{xmlDataGuid}";

        var (service, blobRepoMock) = CreateService(
            instanceGuid,
            CreateFormSummaryDataElements(htmlDataGuid, xmlDataGuid, null),
            "<xml/>"
        );

        // Act
        Stream result = await service.GetFormSummaryAsHtml(
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
        string expectedBlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _appId,
            instanceGuid,
            expectedBlobVersionId
        );

        var (service, blobRepoMock) = CreateService(
            instanceGuid,
            [
                new DataElementInternal
                {
                    Id = signatureDataGuid,
                    DataType = "signature-data",
                    BlobVersionId = expectedBlobVersionId,
                },
            ],
            "[{}]"
        );

        // Act
        Stream result = await service.GetSignatureAsHtml(instanceGuid, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
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
        string expectedBlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _appId,
            instanceGuid,
            expectedBlobVersionId
        );

        var (service, blobRepoMock) = CreateService(
            instanceGuid,
            [
                new DataElementInternal
                {
                    Id = paymentDataGuid,
                    DataType = "payment-data",
                    BlobVersionId = expectedBlobVersionId,
                },
            ],
            "{}"
        );

        // Act
        Stream result = await service.GetPaymentAsHtml(instanceGuid, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
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
    public async Task GetContent_UnknownKind_ReturnsNull()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        var (service, _) = CreateService(instanceGuid, [], "{}");

        // Act
        Stream result = await service.GetContent(
            "somethingelse",
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("payment")]
    [InlineData("formsummaryhtml")]
    [InlineData("formdatahtml")]
    [InlineData("formdatapdf")]
    public async Task GetContent_MissingInstance_ReturnsNull(string kind)
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        IOnDemandContentService service = CreateServiceWithMissingInstance(instanceGuid);

        // Act
        Stream result = await service.GetContent(
            kind,
            _app,
            instanceGuid,
            Guid.NewGuid(),
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFormSummaryAsHtml_WithNoXslViews_ReturnsNull()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();

        var (service, _) = CreateService(
            instanceGuid,
            CreateFormSummaryDataElements(htmlDataGuid, xmlDataGuid, null),
            "<xml/>",
            xsls: []
        );

        // Act
        Stream result = await service.GetFormSummaryAsHtml(
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFormSummaryAsHtml_WhenVisiblePagesExcludeEveryView_ReturnsNull()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();

        List<DataElementInternal> dataElements = CreateFormSummaryDataElements(
            htmlDataGuid,
            xmlDataGuid,
            null
        );
        dataElements[1].Metadata.Add(new KeyValueEntry { Key = "A2VisiblePages", Value = "9" });

        var (service, _) = CreateService(instanceGuid, dataElements, "<xml/>");

        // Act
        Stream result = await service.GetFormSummaryAsHtml(
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFormdataAsHtml_WhenSinglePageNrMatchesNoView_ReturnsNull()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();

        var (service, _) = CreateService(
            instanceGuid,
            CreateFormSummaryDataElements(htmlDataGuid, xmlDataGuid, null),
            "<xml/>",
            xsls: [("<xsl/>", true)]
        );

        // Act
        Stream result = await service.GetFormdataAsHtml(
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None,
            singlePageNr: 5
        );

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFormdataAsPdf_WithNoXslViews_ReturnsNull()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid htmlDataGuid = Guid.NewGuid();
        Guid xmlDataGuid = Guid.NewGuid();

        var (service, _) = CreateService(
            instanceGuid,
            CreateFormSummaryDataElements(htmlDataGuid, xmlDataGuid, null),
            "<xml/>",
            xsls: []
        );

        // Act
        Stream result = await service.GetFormdataAsPdf(
            _app,
            instanceGuid,
            htmlDataGuid,
            "nb",
            CancellationToken.None
        );

        // Assert
        Assert.Null(result);
    }

    private static IOnDemandContentService CreateServiceWithMissingInstance(Guid instanceGuid)
    {
        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        return new OnDemandContentService(
            instanceRepoMock.Object,
            Mock.Of<IBlobRepository>(),
            Mock.Of<IA2Repository>(),
            Mock.Of<IApplicationRepository>(),
            Options.Create(new GeneralSettings()),
            Mock.Of<IA2OndemandFormattingService>(),
            Mock.Of<IPdfGeneratorClient>(),
            CreateViewEngine(),
            Mock.Of<ITempDataProvider>(),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static List<DataElementInternal> CreateFormSummaryDataElements(
        Guid htmlDataGuid,
        Guid xmlDataGuid,
        string? xmlBlobVersionId
    ) =>
        [
            new DataElementInternal
            {
                Id = htmlDataGuid,
                DataType = "html-data",
                Metadata = [new KeyValueEntry { Key = "formid", Value = "1234" }],
            },
            new DataElementInternal
            {
                Id = xmlDataGuid,
                DataType = "xml-data",
                BlobVersionId = xmlBlobVersionId,
                Metadata =
                [
                    new KeyValueEntry { Key = "formid", Value = "1234" },
                    new KeyValueEntry { Key = "lformid", Value = "5678" },
                ],
            },
        ];

    private static (
        IOnDemandContentService Service,
        Mock<IBlobRepository> BlobRepoMock
    ) CreateService(
        Guid instanceGuid,
        List<DataElementInternal> dataElements,
        string blobContent,
        List<(string Xsl, bool IsPortrait)>? xsls = null
    )
    {
        foreach (DataElementInternal dataElement in dataElements)
        {
            dataElement.BlobStoragePath = string.IsNullOrEmpty(dataElement.BlobVersionId)
                ? $"{_org}/{_app}/{instanceGuid}/data/{dataElement.Id}"
                : DataElementHelper.GetVersionedBlobPath(
                    _appId,
                    instanceGuid,
                    dataElement.BlobVersionId
                );
        }

        InstanceInternal instance = new()
        {
            Id = instanceGuid,
            InstanceOwner = new InstanceOwner { PartyId = "555" },
            Org = _org,
            AppId = _appId,
            Data = dataElements,
        };

        Mock<IInstanceRepository> instanceRepoMock = new();
        instanceRepoMock
            .Setup(r => r.GetOne(instanceGuid, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        Mock<IApplicationRepository> appRepoMock = new();
        appRepoMock
            .Setup(r => r.FindOne(_appId, _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = _appId, Org = _org });

        Mock<IA2Repository> a2RepoMock = new();
        a2RepoMock
            .Setup(r => r.GetXsls(_org, _app, 5678, It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(xsls ?? [("<xsl/>", true)]);
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
        formattingMock
            .Setup(f => f.GetFormdataHtml(It.IsAny<PrintViewXslBEList>(), It.IsAny<Stream>()))
            .Returns("<html>test</html>");

        var service = new OnDemandContentService(
            instanceRepoMock.Object,
            blobRepoMock.Object,
            a2RepoMock.Object,
            appRepoMock.Object,
            Options.Create(new GeneralSettings()),
            formattingMock.Object,
            Mock.Of<IPdfGeneratorClient>(),
            CreateViewEngine(),
            Mock.Of<ITempDataProvider>(),
            new ServiceCollection().BuildServiceProvider()
        );

        return (service, blobRepoMock);
    }

    /// <summary>
    /// The signature and payment content is produced by rendering a Razor view, which needs a
    /// view engine that the MVC pipeline would normally supply.
    /// </summary>
    private static ICompositeViewEngine CreateViewEngine()
    {
        Mock<IView> viewMock = new();
        viewMock
            .Setup(v => v.RenderAsync(It.IsAny<ViewContext>()))
            .Returns(
                (ViewContext context) =>
                {
                    context.Writer.Write("<html>rendered</html>");
                    return Task.CompletedTask;
                }
            );

        Mock<ICompositeViewEngine> viewEngineMock = new();
        viewEngineMock
            .Setup(e => e.GetView(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(
                (string _, string path, bool _) => ViewEngineResult.Found(path, viewMock.Object)
            );

        return viewEngineMock.Object;
    }
}
