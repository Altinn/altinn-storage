using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Runs the instance events API against the real repositories and the PostgreSQL test container.
/// </summary>
/// <remarks>
/// In the StoragePostgreSQL collection because other tests in it empty whole tables, such as
/// storage.instances and storage.instanceevents, before each test.
/// </remarks>
[Collection("StoragePostgreSQL")]
public sealed class InstanceEventsControllerDatabaseTests : IAsyncLifetime
{
    private const int _instanceOwnerPartyId = 1337;

    private readonly TestApplicationFactory<Program> _factory = new();
    private readonly Guid _instanceGuid = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        Instance instance = new()
        {
            Id = $"{_instanceOwnerPartyId}/{_instanceGuid}",
            AppId = "tdd/endring-av-navn",
            Org = "tdd",
            InstanceOwner = new InstanceOwner { PartyId = $"{_instanceOwnerPartyId}" },
            Created = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow,
            Status = new InstanceStatus(),
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
        };

        await GetInstanceRepository().Create(instance.FromApiModel(), CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await GetInstanceRepository().Delete(_instanceGuid, CancellationToken.None);
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Post_ThenGet_StoresTheEventInTheDatabase()
    {
        // Arrange
        string requestUri =
            $"storage/api/v1/instances/{_instanceOwnerPartyId}/{_instanceGuid}/events";
        HttpClient client = GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, _instanceOwnerPartyId)
        );
        InstanceEvent instanceEvent = new()
        {
            InstanceId = $"{_instanceOwnerPartyId}/{_instanceGuid}",
            InstanceOwnerPartyId = $"{_instanceOwnerPartyId}",
            EventType = "database-test",
        };

        // Act
        HttpResponseMessage postResponse = await client.PostAsJsonAsync(requestUri, instanceEvent);
        HttpResponseMessage getResponse = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        InstanceEvent? created = await postResponse.Content.ReadFromJsonAsync<InstanceEvent>();
        Assert.NotNull(created?.Id);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        InstanceEventList? events =
            await getResponse.Content.ReadFromJsonAsync<InstanceEventList>();
        InstanceEvent listed = Assert.Single(events!.InstanceEvents);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("database-test", listed.EventType);

        int rows = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instanceevents where alternateid = '{created.Id}'"
        );
        Assert.Equal(1, rows);
    }

    private IInstanceRepository GetInstanceRepository() =>
        ServiceUtil
            .GetServices([typeof(IInstanceRepository)])
            .OfType<IInstanceRepository>()
            .Single();

    private HttpClient GetTestClient() =>
        _factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(new Mock<IKeyVaultClientWrapper>().Object);
                    services.AddSingleton(new Mock<IPartiesWithInstancesClient>().Object);
                    services.AddSingleton(new Mock<IMessageBus>().Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                })
            )
            .CreateClient();
}
