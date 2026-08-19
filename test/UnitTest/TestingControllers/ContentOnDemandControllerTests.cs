#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class ContentOnDemandControllerTests
    : IClassFixture<TestApplicationFactory<ContentOnDemandController>>
{
    private const string _basePath = "storage/api/v1/ondemand";
    private const string _html = "<html><body>formdata</body></html>";

    private readonly TestApplicationFactory<ContentOnDemandController> _factory;

    private const string _org = "ttd";
    private const string _app = "a2-app";
    private const int _instanceOwnerPartyId = 1337;
    private static readonly Guid _instanceGuid = new("1916cd18-3b8e-46f8-aeaf-4bc3397ddd55");
    private static readonly Guid _htmlDataGuid = new("11f7c994-6681-4e3d-a3ba-6b19bbf3e5f6");

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="factory">The web application factory.</param>
    public ContentOnDemandControllerTests(TestApplicationFactory<ContentOnDemandController> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFormdataAsHtml_ReturnsHtmlInResponseBody()
    {
        // Arrange
        HttpClient client = GetTestClient();
        string requestUri = GetRequestUri("formdatahtml");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_html, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetFormdataAsHtml_SinglePageNumber_ReturnsHtmlInResponseBody()
    {
        // Arrange
        HttpClient client = GetTestClient();
        string requestUri = GetRequestUri("formdatahtml/2");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_html, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetFormSummaryAsHtml_ReturnsHtmlInResponseBody()
    {
        // Arrange
        HttpClient client = GetTestClient();
        string requestUri = GetRequestUri("formsummaryhtml");

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_html, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetFormdataAsHtml_XmlElementAtVersionedBlobStoragePath_ReadsStoredPath()
    {
        // Arrange
        Mock<IBlobRepository> blobRepositoryMock = new();
        HttpClient client = GetTestClient(blobRepositoryMock);
        string requestUri = GetRequestUri("formdatahtml");
        string xmlBlobStoragePath = GetInstance()
            .Data.First(d => d.DataType == "a2-xml")
            .BlobStoragePath;

        // Act
        HttpResponseMessage response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        blobRepositoryMock.Verify(
            br =>
                br.ReadBlob(
                    _org,
                    xmlBlobStoragePath,
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private static Instance GetInstance()
    {
        return new Instance
        {
            Id = $"{_instanceOwnerPartyId}/{_instanceGuid}",
            AppId = $"{_org}/{_app}",
            Org = _org,
            InstanceOwner = new InstanceOwner { PartyId = _instanceOwnerPartyId.ToString() },
            Data =
            [
                new DataElement
                {
                    Id = _htmlDataGuid.ToString(),
                    DataType = "ref-data-as-html",
                    BlobStoragePath = "ondemand/formdatahtml",
                    Metadata = [new KeyValueEntry { Key = "formid", Value = "1000" }],
                },
                new DataElement
                {
                    Id = "3a1b2f4c-7a1e-4b25-9f0f-0d6a0f3a5b21",
                    DataType = "a2-xml",
                    BlobStoragePath =
                        $"{_org}/{_app}/{_instanceGuid}/data-elements/AZfQZ9nHc0eLm4Xv2R1qAA",
                    Metadata =
                    [
                        new KeyValueEntry { Key = "formid", Value = "1000" },
                        new KeyValueEntry { Key = "lformid", Value = "2000" },
                    ],
                },
            ],
        };
    }

    private HttpClient GetTestClient(Mock<IBlobRepository> blobRepositoryMock = null)
    {
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(_instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (GetInstance(), 1L));

        Mock<IApplicationRepository> applicationRepositoryMock = new();
        applicationRepositoryMock
            .Setup(ar => ar.FindOne($"{_org}/{_app}", _org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = $"{_org}/{_app}", Org = _org });

        List<(string Xsl, bool IsPortrait)> xsls =
        [
            ("<xsl:stylesheet>page1</xsl:stylesheet>", true),
            ("<xsl:stylesheet>page2</xsl:stylesheet>", false),
        ];
        Mock<IA2Repository> a2RepositoryMock = new();
        a2RepositoryMock
            .Setup(ar => ar.GetXsls(_org, _app, 2000, "nb", It.IsAny<int>()))
            .ReturnsAsync(xsls);

        blobRepositoryMock ??= new();
        blobRepositoryMock
            .Setup(br =>
                br.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("<form/>")));

        Mock<IA2OndemandFormattingService> formattingServiceMock = new();
        formattingServiceMock
            .Setup(f => f.GetFormdataHtml(It.IsAny<PrintViewXslBEList>(), It.IsAny<Stream>()))
            .Returns(_html);

        HttpClient client = _factory
            .WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration(
                    (hostingContext, config) =>
                    {
                        config.AddConfiguration(configuration);
                    }
                );

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(instanceRepositoryMock.Object);
                    services.AddSingleton(applicationRepositoryMock.Object);
                    services.AddSingleton(a2RepositoryMock.Object);
                    services.AddSingleton(blobRepositoryMock.Object);
                    services.AddSingleton(formattingServiceMock.Object);
                });
            })
            .CreateClient();

        return client;
    }

    private static string GetRequestUri(string endpoint)
    {
        return $"{_basePath}/{_org}/{_app}/{_instanceOwnerPartyId}/{_instanceGuid}/{_htmlDataGuid}"
            + $"/nb/{endpoint}";
    }
}
