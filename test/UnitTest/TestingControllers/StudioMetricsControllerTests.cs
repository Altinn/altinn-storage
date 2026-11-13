#nullable disable

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class StudioMetricsControllerTests : IAsyncLifetime
{
    private readonly TestApplicationFactory<StudioMetricsController> _factory = new();

    private const string BasePath = "/storage/api/v1/studio/metrics";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetDailyMetrics_NoAccessToken_ReturnsUnauthorized()
    {
        // Arrange
        HttpClient client = GetTestClient(new Mock<IStudioMetricsRepository>().Object);

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd?date=2026-11-09");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDailyMetrics_WrongAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(
            new Mock<IStudioMetricsRepository>().Object,
            tokenAppId: "studioo.designer"
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd?date=2026-11-09");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=")]
    [InlineData("?date=2026-13-01")]
    [InlineData("?date=yesterday")]
    public async Task GetDailyMetrics_MissingOrInvalidDate_ReturnsBadRequest(string query)
    {
        // Arrange
        var repositoryMock = new Mock<IStudioMetricsRepository>();
        HttpClient client = GetAuthenticatedClient(repositoryMock.Object);

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd{query}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repositoryMock.Verify(
            r =>
                r.GetDailyMetrics(
                    It.IsAny<string>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task GetDailyMetrics_ReturnsMetricsForOrgAndDate()
    {
        // Arrange
        StudioDailyMetrics metrics = new()
        {
            Org = "ttd",
            Date = new DateOnly(2026, 11, 9),
            ComputedAt = new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc),
            Apps =
            [
                new()
                {
                    App = "kpi-a",
                    InstancesCreated = 2,
                    InstancesCompleted = 1,
                    SecondsToCompleteP50 = 600,
                    OpenInstances = 3,
                    OpenOver90Days = 1,
                    OpenByTask = [new() { TaskId = "Task_1", OpenInstances = 3 }],
                },
            ],
        };
        var repositoryMock = new Mock<IStudioMetricsRepository>();
        repositoryMock
            .Setup(r =>
                r.GetDailyMetrics("ttd", new DateOnly(2026, 11, 9), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(metrics);
        HttpClient client = GetAuthenticatedClient(repositoryMock.Object);

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd?date=2026-11-09");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        StudioDailyMetrics actual = JsonConvert.DeserializeObject<StudioDailyMetrics>(content);
        Assert.Equal(metrics, actual with { Apps = metrics.Apps });
        StudioAppDailyMetrics app = Assert.Single(actual.Apps);
        Assert.Equal(metrics.Apps[0] with { OpenByTask = app.OpenByTask }, app);
        Assert.Equal(metrics.Apps[0].OpenByTask, app.OpenByTask);
        Assert.Contains("\"secondsToCompleteP50\":600", content);
    }

    [Fact]
    public async Task GetDailyMetrics_RepositoryFails_ReturnsInternalServerError()
    {
        // Arrange
        var repositoryMock = new Mock<IStudioMetricsRepository>();
        repositoryMock
            .Setup(r =>
                r.GetDailyMetrics(
                    It.IsAny<string>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("boom"));
        HttpClient client = GetAuthenticatedClient(repositoryMock.Object);

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd?date=2026-11-09");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private HttpClient GetAuthenticatedClient(
        IStudioMetricsRepository repository,
        string tokenAppId = "studio.designer"
    )
    {
        HttpClient client = GetTestClient(repository);
        string token = PrincipalUtil.GetAccessToken(tokenAppId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient GetTestClient(IStudioMetricsRepository repository)
    {
        return _factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(repository);
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
