using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
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
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class PartyInstancesControllerTests(TestApplicationFactory<PartyInstancesController> factory)
    : IClassFixture<TestApplicationFactory<PartyInstancesController>>
{
    private readonly TestApplicationFactory<PartyInstancesController> _factory = factory;

    private const string _basePath = "storage/api/v1/parties";

    private const string _scope = "altinn:storage/instances.supportdashboard";

    private const int _partyId = 1337;

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new(MockBehavior.Strict);
        HttpClient client = GetTestClient(repositoryMock);
        using HttpRequestMessage message = new(HttpMethod.Get, $"{_basePath}/{_partyId}/instances");

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        repositoryMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("altinn:storage/instances.syncadapter")]
    [InlineData("altinn:serviceowner/instances.read")]
    public async Task Get_WithoutSupportDashboardScope_ReturnsForbidden(string scope)
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new(MockBehavior.Strict);
        HttpClient client = GetTestClient(repositoryMock);
        using HttpRequestMessage message = new(HttpMethod.Get, $"{_basePath}/{_partyId}/instances");
        AddToken(message, scope);

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        repositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_FirstBatch_ReturnsInstancesWithDataElementsAndNextLink()
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(r => r.GetInstancesForParty(_partyId, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceQueryResult
                {
                    Instances =
                    [
                        CreateInstance(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1),
                        CreateInstance(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), 2),
                    ],
                    ContinuationToken = "638424288000000000;2",
                }
            );

        HttpClient client = GetTestClient(repositoryMock);

        // Act
        QueryResponse<Instance> response = await SendAsync(
            client,
            $"{_basePath}/{_partyId}/instances"
        );

        // Assert
        Assert.Equal(2, response.Count);
        Assert.Equal(2, response.Instances.Count);
        Assert.Equal(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            response.Instances[0].Created
        );
        Assert.Single(response.Instances[0].Data);
        Assert.EndsWith(
            "/storage/api/v1/parties/1337/instances?continuationToken=638424288000000000;2",
            response.Next
        );
    }

    [Fact]
    public async Task Get_LastBatch_OmitsNextLink()
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(r =>
                r.GetInstancesForParty(
                    _partyId,
                    50,
                    It.IsAny<InstanceContinuationToken?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceQueryResult
                {
                    Instances = [CreateInstance(DateTime.UtcNow, 9)],
                    ContinuationToken = null,
                }
            );

        HttpClient client = GetTestClient(repositoryMock);

        // Act
        QueryResponse<Instance> response = await SendAsync(
            client,
            $"{_basePath}/{_partyId}/instances"
        );

        // Assert
        Assert.Equal(1, response.Count);
        Assert.Null(response.Next);
    }

    [Fact]
    public async Task Get_WithContinuationToken_PassesCursorToRepository()
    {
        // Arrange
        DateTime created = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        InstanceContinuationToken expected = new(created, 42);

        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(r =>
                r.GetInstancesForParty(_partyId, 25, expected, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new InstanceQueryResult());

        HttpClient client = GetTestClient(repositoryMock);
        string token = Uri.EscapeDataString($"{created.Ticks};42");

        // Act
        QueryResponse<Instance> response = await SendAsync(
            client,
            $"{_basePath}/{_partyId}/instances?size=25&continuationToken={token}"
        );

        // Assert
        Assert.Equal(0, response.Count);
        repositoryMock.Verify(
            r => r.GetInstancesForParty(_partyId, 25, expected, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Theory]
    [InlineData("continuationToken=not-a-token", "The continuation token is not valid.")]
    [InlineData("continuationToken=123", "The continuation token is not valid.")]
    [InlineData("size=0", "The size must be between 1 and 100.")]
    [InlineData("size=101", "The size must be between 1 and 100.")]
    public async Task Get_WithInvalidParameters_ReturnsBadRequest(
        string queryString,
        string expectedMessage
    )
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new(MockBehavior.Strict);
        HttpClient client = GetTestClient(repositoryMock);
        using HttpRequestMessage message = new(
            HttpMethod.Get,
            $"{_basePath}/{_partyId}/instances?{queryString}"
        );
        AddToken(message);

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync());
        repositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WhenRepositoryFails_ReturnsInternalServerError()
    {
        // Arrange
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(r =>
                r.GetInstancesForParty(
                    _partyId,
                    50,
                    It.IsAny<InstanceContinuationToken?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceQueryResult { Exception = "database is on fire" });

        HttpClient client = GetTestClient(repositoryMock);
        using HttpRequestMessage message = new(HttpMethod.Get, $"{_basePath}/{_partyId}/instances");
        AddToken(message);

        // Act
        using HttpResponseMessage response = await client.SendAsync(message);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("database is on fire", await response.Content.ReadAsStringAsync());
    }

    private static InstanceInternal CreateInstance(DateTime created, long internalId) =>
        new()
        {
            Id = Guid.NewGuid(),
            InternalId = internalId,
            AppId = "tdd/test-app",
            Org = "tdd",
            Created = created,
            LastChanged = created,
            InstanceOwner = new InstanceOwner { PartyId = _partyId.ToString() },
            Status = new InstanceStatus(),
            Data = [new DataElementInternal { Id = Guid.NewGuid(), DataType = "default" }],
        };

    private static void AddToken(HttpRequestMessage message, string scope = _scope)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetOrgToken("supportdashboard", scope: scope)
        );
    }

    private static async Task<QueryResponse<Instance>> SendAsync(HttpClient client, string uri)
    {
        using HttpRequestMessage message = new(HttpMethod.Get, uri);
        AddToken(message);

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonConvert.DeserializeObject<QueryResponse<Instance>>(
            await response.Content.ReadAsStringAsync()
        )!;
    }

    private HttpClient GetTestClient(Mock<IInstanceRepository> repositoryMock)
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
                    services.AddSingleton(repositoryMock.Object);
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
