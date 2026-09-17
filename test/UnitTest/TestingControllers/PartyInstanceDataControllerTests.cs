using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class PartyInstanceDataControllerTests(
    TestApplicationFactory<PartyInstanceDataController> factory
) : IClassFixture<TestApplicationFactory<PartyInstanceDataController>>
{
    private readonly TestApplicationFactory<PartyInstanceDataController> _factory = factory;

    private const string _scope = "altinn:storage/data.supportdashboard";

    private const int _partyId = 1337;

    private static readonly Guid _instanceGuid = new("6e1e3f2c-0f2b-4f8a-9b0f-5a4e2c7d1b33");

    private static readonly Guid _dataGuid = new("a1b2c3d4-0000-4f8a-9b0f-5a4e2c7d1b33");

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        Mock<IDataElementContentService> serviceMock = new(MockBehavior.Strict);
        HttpClient client = GetTestClient(serviceMock);
        using HttpRequestMessage message = new(HttpMethod.Get, DataUri());

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("altinn:storage/instances.supportdashboard")]
    [InlineData("altinn:serviceowner/instances.read")]
    public async Task Get_WithoutDataSupportDashboardScope_ReturnsForbidden(string scope)
    {
        // Arrange
        Mock<IDataElementContentService> serviceMock = new(MockBehavior.Strict);
        HttpClient client = GetTestClient(serviceMock);
        using HttpRequestMessage message = new(HttpMethod.Get, DataUri());
        AddToken(message, scope);

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_BlobContent_ReturnsFileWithBlobVersionETag()
    {
        // Arrange
        DataElementReadContext context = CreateContext();
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);
        serviceMock
            .Setup(s => s.OpenContent(context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("file contents")));

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("file contents", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("form.pdf", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("\"AAAAAAAAAAAAAAAAAAAAAA\"", response.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Get_DoesNotMarkTheDataElementAsRead()
    {
        // Arrange
        DataElementReadContext context = CreateContext();
        Mock<IDataRepository> dataRepositoryMock = new(MockBehavior.Strict);
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);
        serviceMock
            .Setup(s => s.OpenContent(context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("file contents")));

        HttpClient client = GetTestClient(serviceMock, dataRepositoryMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        dataRepositoryMock.Verify(
            r =>
                r.UpdateReadStatus(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Get_OnDemandContent_ReturnsInlineFile()
    {
        // Arrange
        DataElementReadContext context = CreateContext(blobStoragePath: "ondemand/ttd/formdata");
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);
        serviceMock
            .Setup(s => s.OpenContent(context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("<html></html>")));

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Null(response.Headers.ETag);
    }

    [Fact]
    public async Task Get_HardDeletedDataElement_ReturnsNotFound()
    {
        // Arrange
        DataElementReadContext context = CreateContext();
        context.DataElement.DeleteStatus = new DeleteStatus { IsHardDeleted = true };
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        serviceMock.Verify(
            s =>
                s.OpenContent(
                    It.IsAny<DataElementReadContext>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Get_InstanceOwnedByAnotherParty_ReturnsNotFound()
    {
        // Arrange
        DataElementReadContext context = CreateContext();
        context.Instance.InstanceOwner.PartyId = "1338";
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        serviceMock.Verify(
            s =>
                s.OpenContent(
                    It.IsAny<DataElementReadContext>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Theory]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    public async Task Get_WhenResolveFails_ReturnsTheServiceErrorStatusCode(
        int errorCode,
        HttpStatusCode expected
    )
    {
        // Arrange
        Mock<IDataElementContentService> serviceMock = new();
        serviceMock
            .Setup(s =>
                s.ResolveForRead(_partyId, _instanceGuid, _dataGuid, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((null, new ServiceError(errorCode, "nope")));

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenContentCannotBeRead_ReturnsNotFound()
    {
        // Arrange
        DataElementReadContext context = CreateContext();
        Mock<IDataElementContentService> serviceMock = CreateServiceMock(context);
        serviceMock
            .Setup(s => s.OpenContent(context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream)null!);

        HttpClient client = GetTestClient(serviceMock);

        // Act
        using HttpResponseMessage response = await SendAsync(client, DataUri());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string DataUri(int partyId = _partyId) =>
        $"storage/api/v1/parties/{partyId}/instances/{_instanceGuid}/data/{_dataGuid}";

    private static Mock<IDataElementContentService> CreateServiceMock(
        DataElementReadContext context
    )
    {
        Mock<IDataElementContentService> serviceMock = new();
        serviceMock
            .Setup(s =>
                s.ResolveForRead(_partyId, _instanceGuid, _dataGuid, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((context, null));

        return serviceMock;
    }

    private static DataElementReadContext CreateContext(
        string blobStoragePath =
            "tdd/test-app/6e1e3f2c-0f2b-4f8a-9b0f-5a4e2c7d1b33/data/a1b2c3d4-0000-4f8a-9b0f-5a4e2c7d1b33"
    ) =>
        new(
            _instanceGuid,
            _dataGuid,
            new InstanceInternal
            {
                Id = _instanceGuid,
                AppId = "tdd/test-app",
                Org = "tdd",
                InstanceOwner = new InstanceOwner { PartyId = _partyId.ToString() },
                Status = new InstanceStatus(),
            },
            new DataElementInternal
            {
                Id = _dataGuid,
                DataType = "default",
                ContentType = "application/pdf",
                Filename = "form.pdf",
                BlobStoragePath = blobStoragePath,
                BlobVersionId = "AAAAAAAAAAAAAAAAAAAAAA",
            },
            new Application { Id = "tdd/test-app", Org = "tdd" }
        );

    private static void AddToken(HttpRequestMessage message, string scope = _scope)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetOrgToken("supportdashboard", scope: scope)
        );
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string uri)
    {
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        AddToken(message);

        return await client.SendAsync(message);
    }

    private HttpClient GetTestClient(
        Mock<IDataElementContentService> serviceMock,
        Mock<IDataRepository>? dataRepositoryMock = null
    )
    {
        return _factory
            .WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration(
                    (_, config) => config.AddConfiguration(configuration)
                );

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(serviceMock.Object);
                    if (dataRepositoryMock is not null)
                    {
                        services.AddSingleton(dataRepositoryMock.Object);
                    }

                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                });
            })
            .CreateClient();
    }
}
