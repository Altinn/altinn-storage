#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;
using static Altinn.Platform.Storage.Interface.Models.SignRequest;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class SignControllerTests : IClassFixture<TestApplicationFactory<SignController>>
{
    private const string BasePath = "storage/api/v1/instances";

    private readonly TestApplicationFactory<SignController> _factory;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="factory">The web application factory.</param>
    public SignControllerTests(TestApplicationFactory<SignController> factory)
    {
        _factory = factory;
    }

    public static TheoryData<Signee> SigneeData =>
        new(
            new Signee()
            {
                UserId = "1337",
                PersonNumber = "22117612345",
                SystemUserId = null,
                OrganisationNumber = null,
            },
            new Signee()
            {
                PersonNumber = null,
                SystemUserId = Guid.Parse("f58fe166-bc22-4899-beb7-c3e8e3332f43"),
                OrganisationNumber = "524446332",
            }
        );

    public static TheoryData<StorageVersionMismatchException, string> VersionMismatchData =>
        new()
        {
            {
                new InstanceVersionMismatchException(9, 4),
                "{\"type\":\"instance_version_mismatch\",\"title\":\"Instance version did not match expected version.\",\"status\":412}"
            },
            {
                new ProcessStateVersionMismatchException(9, 4),
                "{\"type\":\"process_state_version_mismatch\",\"title\":\"Process state version did not match expected version.\",\"status\":412}"
            },
        };

    [Theory]
    [MemberData(nameof(SigneeData))]
    public async Task SignRequest_UserHasRequiredRole_Created(Signee signee)
    {
        // Arrange
        int instanceOwnerPartyId = 1600;
        string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

        Mock<ISigningService> instanceServiceMock = new Mock<ISigningService>();
        instanceServiceMock
            .Setup(ism =>
                ism.CreateSignDocument(
                    It.IsAny<Guid>(),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync(SignDocumentCreateResult.Success(new StorageVersions(1, 1)));

        HttpClient client = GetTestClient(instanceServiceMock);
        string token = !string.IsNullOrWhiteSpace(signee.UserId)
            ? PrincipalUtil.GetToken(10016, 1600, 2)
            : PrincipalUtil.GetSystemUserToken(
                signee.SystemUserId.ToString(),
                signee.OrganisationNumber
            );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures = new List<DataElementSignature>
            {
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            },
            Signee = signee,
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json"))
        );

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SignRequest_UserDoesNotHaveRequiredRole_Forbidden()
    {
        // Arrange
        int instanceOwnerPartyId = 1600;
        string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(43, 12800, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        SignRequest signRequest = new SignRequest();

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json"))
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignRequest_UserHasRequiredRole_InvalidUserId_BadRequest()
    {
        // Arrange
        int instanceOwnerPartyId = 1600;
        string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

        HttpClient client = GetTestClient();
        string token = PrincipalUtil.GetToken(10016, 1600, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        SignRequest signRequest = new SignRequest();

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json"))
        );

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignRequest_UserHasRequiredRole_InstanceServiceFail_NotFound()
    {
        // Arrange
        int instanceOwnerPartyId = 1600;
        string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";

        Mock<ISigningService> instanceServiceMock = new Mock<ISigningService>();
        instanceServiceMock
            .Setup(ism =>
                ism.CreateSignDocument(
                    It.IsAny<Guid>(),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync(
                SignDocumentCreateResult.Failure(new ServiceError(404, "Instance not found"))
            );

        HttpClient client = GetTestClient(instanceServiceMock);
        string token = PrincipalUtil.GetToken(10016, 1600, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        SignRequest signRequest = new SignRequest
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures = new List<DataElementSignature>
            {
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            },
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            requestUri,
            JsonContent.Create(signRequest, new MediaTypeHeaderValue("application/json"))
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task SignRequest_RepositoryExceptionWithStatusSuggestion_ReturnsSuggestedStatus(
        HttpStatusCode statusCodeSuggestion
    )
    {
        const int instanceOwnerPartyId = 1600;
        const string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";
        Mock<ISigningService> signingService = new();
        signingService
            .Setup(service =>
                service.CreateSignDocument(
                    It.IsAny<Guid>(),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ThrowsAsync(
                new RepositoryException("Data element is not available.", statusCodeSuggestion)
            );
        HttpClient client = GetTestClient(signingService);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(10016, instanceOwnerPartyId, 2)
        );
        SignRequest signRequest = new()
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(requestUri, signRequest);

        Assert.Equal(statusCodeSuggestion, response.StatusCode);
        Assert.Contains(
            "Data element is not available.",
            await response.Content.ReadAsStringAsync()
        );
    }

    [Theory]
    [MemberData(nameof(VersionMismatchData))]
    public async Task SignRequest_VersionMismatch_ReturnsSharedByteExactProblemDetails(
        StorageVersionMismatchException exception,
        string expectedBody
    )
    {
        const int instanceOwnerPartyId = 1600;
        const string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";
        Mock<ISigningService> signingService = new();
        signingService
            .Setup(service =>
                service.CreateSignDocument(
                    It.IsAny<Guid>(),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ThrowsAsync(exception);
        HttpClient client = GetTestClient(signingService);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(10016, instanceOwnerPartyId, 2)
        );
        SignRequest signRequest = new()
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(requestUri, signRequest);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(
            "application/json; charset=utf-8",
            response.Content.Headers.ContentType?.ToString()
        );
        Assert.Equal("9", response.Headers.GetValues(StorageHeaders.InstanceVersion).Single());
        Assert.Equal("4", response.Headers.GetValues(StorageHeaders.ProcessStateVersion).Single());
        Assert.Equal(
            Encoding.UTF8.GetBytes(expectedBody),
            await response.Content.ReadAsByteArrayAsync()
        );
    }

    [Fact]
    public async Task SignRequest_ProcessStatusConflict_ReturnsConflictWithCurrentStatus()
    {
        const int instanceOwnerPartyId = 1600;
        const string instanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd55";
        string requestUri = $"{BasePath}/{instanceOwnerPartyId}/{instanceGuid}/sign";
        Mock<ISigningService> signingService = new();
        signingService
            .Setup(service =>
                service.CreateSignDocument(
                    It.IsAny<Guid>(),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing));
        HttpClient client = GetTestClient(signingService);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(10016, instanceOwnerPartyId, 2)
        );
        SignRequest signRequest = new()
        {
            SignatureDocumentDataType = "sign-data-type",
            DataElementSignatures =
            [
                new DataElementSignature
                {
                    DataElementId = Guid.NewGuid().ToString(),
                    Signed = true,
                },
            ],
            Signee = new Signee { UserId = "1337", PersonNumber = "22117612345" },
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(requestUri, signRequest);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            ProcessStatus.Processing,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal
        );
        signingService.Verify(
            service =>
                service.CreateSignDocument(
                    Guid.Parse(instanceGuid),
                    It.IsAny<SignRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                ),
            Times.Once
        );
    }

    private HttpClient GetTestClient(Mock<ISigningService> instanceServiceMock = null)
    {
        // No setup required for these services. They are not in use by the InstanceController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
        Mock<IMessageBus> busMock = new Mock<IMessageBus>();

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
                    if (instanceServiceMock != null)
                    {
                        services.AddSingleton(instanceServiceMock.Object);
                    }

                    services.AddMockRepositories();
                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton<
                        IPartiesWithInstancesClient,
                        PartiesWithInstancesClientMock
                    >();
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton(busMock.Object);
                });
            })
            .CreateClient();

        return client;
    }
}
