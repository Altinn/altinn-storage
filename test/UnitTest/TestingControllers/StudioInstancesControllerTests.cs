#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
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
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class StudioInstancesControllerTests
    : IClassFixture<TestApplicationFactory<StudioInstancesController>>
{
    private readonly TestApplicationFactory<StudioInstancesController> _factory;
    private const string BasePath = "/storage/api/v1/studio/instances";

    public StudioInstancesControllerTests(TestApplicationFactory<StudioInstancesController> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetInstances_NoAccessToken_ReturnsUnauthorized()
    {
        // Arrange
        HttpClient client = GetTestClient();

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInstances_NoAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: null);

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetInstances_WrongAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: "studioo.designer");

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetInstances_ReturnsOk()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir =>
                ir.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    false,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceQueryResponse
                {
                    Instances = new List<Instance>
                    {
                        new Instance
                        {
                            Id = "1337/guid",
                            InstanceOwner = new() { PartyId = "1337" },
                            AppId = "ttd/app",
                            Org = "ttd",
                        },
                    },
                }
            );

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        var queryResponse = JsonConvert.DeserializeObject<QueryResponse<SimpleInstance>>(content);
        Assert.Single(queryResponse.Instances);
    }

    [Fact]
    public async Task GetInstances_RepositoryReturnsException_Returns500()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir =>
                ir.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    false,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceQueryResponse { Exception = "Something went wrong" });

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetInstances_RepositoryThrowsException_Returns500()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir =>
                ir.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    false,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new Exception("Database connection error"));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetInstances_WithContinuationToken_ReturnsOk()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir =>
                ir.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    false,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceQueryResponse
                {
                    Instances = new List<Instance>(),
                    ContinuationToken = "nextToken",
                }
            );

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app?continuationToken=someToken"
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        var queryResponse = JsonConvert.DeserializeObject<QueryResponse<SimpleInstance>>(content);
        Assert.Equal(System.Web.HttpUtility.UrlEncode("nextToken"), queryResponse.Next);
    }

    [Fact]
    public async Task GetSingleInstance_NoAccessToken_ReturnsUnauthorized()
    {
        // Arrange
        HttpClient client = GetTestClient();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_NoAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: null);

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_WrongAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: "studioo.designer");

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_ReturnsOk()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app/{instanceGuid}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        var simpleInstanceDetails = JsonConvert.DeserializeObject<SimpleInstanceDetails>(content);
        Assert.Equal(instanceGuid.ToString(), simpleInstanceDetails.Id);
    }

    [Fact]
    public async Task GetSingleInstance_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, 0));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_OrgMismatch_ReturnsNotFound()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "skd", // Mismatch
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app/{instanceGuid}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_AppMismatch_ReturnsNotFound()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/some-other-app", // Mismatch
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app/{instanceGuid}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSingleInstance_RepositoryThrowsException_Returns500()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection error"));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_NoAccessToken_ReturnsUnauthorized()
    {
        // Arrange
        HttpClient client = GetTestClient();

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_NoAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: null);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_WrongAppClaim_ReturnsForbidden()
    {
        // Arrange
        HttpClient client = GetAuthenticatedClient(tokenAppId: "studioo.designer");

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, 0));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{Guid.NewGuid()}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_GetApplicationOrErrorAsyncReturnsNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((null, new ServiceError(404, "Application not found")));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_GetApplicationOrErrorAsyncReturnsServerError_Returns500()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((null, new ServiceError(500, "Something went wrong")));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_InstancePreventedFromDeletion_ReturnsForbidden()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var archived = DateTime.UtcNow.AddDays(-5);
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
            Status = new InstanceStatus { Archived = archived },
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));

        var application = new Application { PreventInstanceDeletionForDays = 30 };
        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((application, null));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );
        string responseMessage = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "Instance cannot be deleted yet due to application restrictions.",
            responseMessage
        );
    }

    [Fact]
    public async Task DeleteInstance_RepositoryUpdateThrowsException_Returns500()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));
        instanceRepositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<Instance>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new Exception("Database connection error"));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_ReturnsNoContent()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var instance = new Instance
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, 1));
        instanceRepositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<Instance>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((Instance i, List<string> _, CancellationToken _) => i);

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object,
            instanceEventService: instanceEventServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        instanceRepositoryMock.Verify(
            ir =>
                ir.Update(
                    It.Is<Instance>(i =>
                        i.Status.IsSoftDeleted == true && i.Status.SoftDeleted != null
                    ),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        instanceEventServiceMock.Verify(
            s => s.DispatchEvent(InstanceEventType.Deleted, It.IsAny<Instance>()),
            Times.Once
        );
    }

    private HttpClient GetAuthenticatedClient(
        IInstanceRepository instanceRepository = null,
        IApplicationService applicationService = null,
        IInstanceEventService instanceEventService = null,
        string tokenAppId = "studio.designer"
    )
    {
        HttpClient client = GetTestClient(
            instanceRepository,
            applicationService,
            instanceEventService
        );
        string token = PrincipalUtil.GetAccessToken(tokenAppId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient GetTestClient(
        IInstanceRepository instanceRepository = null,
        IApplicationService applicationService = null,
        IInstanceEventService instanceEventService = null
    )
    {
        if (instanceRepository == null)
        {
            instanceRepository = new Mock<IInstanceRepository>().Object;
        }

        var client = _factory
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
                    services.AddSingleton(instanceRepository);
                    if (applicationService != null)
                    {
                        services.AddSingleton(applicationService);
                    }

                    if (instanceEventService != null)
                    {
                        services.AddSingleton(instanceEventService);
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

        return client;
    }
}
