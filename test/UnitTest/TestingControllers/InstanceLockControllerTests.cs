#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Test class for Instance Lock Controller.
/// </summary>
public class InstanceLockControllerTest
    : IClassFixture<TestApplicationFactory<InstanceLockController>>
{
    private readonly TestApplicationFactory<InstanceLockController> _factory;
    private readonly Guid _instanceGuid = new("20a1353e-91cf-44d6-8ff7-f68993638ffe");
    private readonly long _instanceInternalId = 4567;
    private readonly int _userId = 3;
    private readonly int _partyId = 1337;

    public InstanceLockControllerTest(TestApplicationFactory<InstanceLockController> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Test: Verifies that AcquireInstanceLock and UpdateInstanceLock endpoints have authorization attributes.
    /// Expected: Both should have the [Authorize] attribute with no policy.
    /// </summary>
    [Fact]
    public void InstanceLockEndpoints_ShouldHaveAuthorizeAttribute()
    {
        var controllerType = typeof(InstanceLockController);
        var acquireLockMethod = controllerType.GetMethod(
            nameof(InstanceLockController.AcquireInstanceLock)
        );
        var updateLockMethod = controllerType.GetMethod(
            nameof(InstanceLockController.UpdateInstanceLock)
        );

        var acquireAuthorizeAttribute = acquireLockMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(acquireAuthorizeAttribute);
        Assert.Null(acquireAuthorizeAttribute.Policy);

        var updateAuthorizeAttribute = updateLockMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(updateAuthorizeAttribute);
        Assert.Null(updateAuthorizeAttribute.Policy);
    }

    /// <summary>
    /// Test case: User acquires a lock on a valid instance
    /// Expected: Returns 200 OK with a valid lock token
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_ValidInstance_LockAcquired_ReturnsOkWithLockToken()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };
        var expectedLockId = 12345;
        var expectedLockSecret = new byte[20];
        new Random().NextBytes(expectedLockSecret);
        var expectedLockToken = new LockToken(expectedLockId, expectedLockSecret);

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryAcquireLock(
                    _instanceInternalId,
                    300,
                    _userId.ToString(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((AcquireLockResult.Success, expectedLockToken));

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);
        var instanceLockResponse = await response.Content.ReadFromJsonAsync<InstanceLockResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(instanceLockResponse);
        Assert.NotNull(instanceLockResponse.LockToken);

        var parsedLockToken = LockToken.ParseToken(instanceLockResponse.LockToken);
        Assert.Equal(expectedLockId, parsedLockToken.Id);
        Assert.Equal(expectedLockSecret, parsedLockToken.Secret);
    }

    /// <summary>
    /// Test case: Instance not found when trying to acquire lock
    /// Expected: Returns 404 Not Found with problem details
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceGuid = Guid.NewGuid();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Lock is already held by another process
    /// Expected: Returns 409 Conflict with problem details
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_LockAlreadyHeld_ReturnsConflict()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryAcquireLock(
                    _instanceInternalId,
                    300,
                    _userId.ToString(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((AcquireLockResult.LockAlreadyHeld, null));

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Negative TTL is provided
    /// Expected: Returns 400 Bad Request with problem details
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_NegativeTtl_ReturnsBadRequest()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = -1 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: User updates an existing lock with valid lock token
    /// Expected: Returns 204 No Content
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_ValidLockToken_ReturnsNoContent()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryUpdateLockExpiration(
                    It.Is<LockToken>(token =>
                        token.Id == lockId && token.Secret.SequenceEqual(lockSecret)
                    ),
                    _instanceInternalId,
                    300,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(UpdateLockResult.Success);

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Test case: Lock token doesn't correspond to an existing lock
    /// Expected: Returns 404 Not Found with problem details
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_LockNotFound_ReturnsNotFound()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryUpdateLockExpiration(
                    It.Is<LockToken>(token =>
                        token.Id == lockId && token.Secret.SequenceEqual(lockSecret)
                    ),
                    _instanceInternalId,
                    300,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(UpdateLockResult.LockNotFound);

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Lock exists but has already expired
    /// Expected: Returns 422 Unprocessable Entity with problem details
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_LockExpired_ReturnsUnprocessableEntity()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryUpdateLockExpiration(
                    It.Is<LockToken>(token =>
                        token.Id == lockId && token.Secret.SequenceEqual(lockSecret)
                    ),
                    _instanceInternalId,
                    300,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(UpdateLockResult.LockExpired);

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Negative TTL is provided for update
    /// Expected: Returns 400 Bad Request with problem details
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_NegativeTtl_ReturnsBadRequest()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = -1 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Instance doesn't exist
    /// Expected: Returns 404 Not Found with problem details
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var instanceGuid = Guid.NewGuid();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: User is not authorized to acquire instance lock
    /// Expected: Returns 403 Forbidden with problem details
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_UserNotAuthorized_ReturnsForbidden()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(-1, 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Instance exists but instanceOwnerPartyId doesn't match
    /// Expected: Returns 404 Not Found with problem details
    /// </summary>
    [Fact]
    public async Task AcquireInstanceLock_InstanceOwnerPartyIdMismatch_ReturnsNotFound()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/9999/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PostAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Instance exists but instanceOwnerPartyId doesn't match when updating lock
    /// Expected: Returns 404 Not Found with problem details
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_InstanceOwnerPartyIdMismatch_ReturnsNotFound()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/9999/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Missing Altinn-Storage-Lock-Token header when updating lock
    /// Expected: Returns 400 Bad Request
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_MissingLockTokenHeader_ReturnsBadRequest()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Invalid lock token format in Altinn-Storage-Lock-Token header
    /// Expected: Returns 400 Bad Request
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_InvalidLockTokenFormat_ReturnsBadRequest()
    {
        // Arrange
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        var client = GetTestClient();
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", "invalid-token");

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    /// <summary>
    /// Test case: Lock token doesn't match the stored token hash
    /// Expected: Returns 403 Forbidden
    /// </summary>
    [Fact]
    public async Task UpdateInstanceLock_LockTokenMismatch_ReturnsForbidden()
    {
        // Arrange
        var lockId = 12345;
        var lockSecret = new byte[20];
        new Random().NextBytes(lockSecret);
        var lockToken = new LockToken(lockId, lockSecret).CreateToken();
        var requestUri = $"storage/api/v1/instances/{_partyId}/{_instanceGuid}/lock";

        var lockRequest = new InstanceLockRequest { TtlSeconds = 300 };

        Mock<IInstanceLockRepository> instanceLockRepoMock = new();
        instanceLockRepoMock
            .Setup(r =>
                r.TryUpdateLockExpiration(
                    It.Is<LockToken>(token =>
                        token.Id == lockId && token.Secret.SequenceEqual(lockSecret)
                    ),
                    _instanceInternalId,
                    300,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(UpdateLockResult.TokenMismatch);

        var client = GetTestClient(instanceLockRepository: instanceLockRepoMock.Object);
        var token = PrincipalUtil.GetToken(_userId, _partyId, 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Altinn-Storage-Lock-Token", lockToken);

        // Act
        using var response = await client.PatchAsJsonAsync(requestUri, lockRequest);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    private HttpClient GetTestClient(
        IInstanceRepository? instanceRepository = null,
        IInstanceAndEventsRepository? instanceAndEventsRepository = null,
        IInstanceLockRepository? instanceLockRepository = null,
        bool enableWolverine = false
    )
    {
        // No setup required for these services. They are not in use by the ApplicationController
        var keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
        var partiesWrapper = new Mock<IPartiesWithInstancesClient>();

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
                    services.AddMockRepositories();

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<IInstanceEventRepository, InstanceEventRepositoryMock>();
                    services.Configure<WolverineSettings>(opts =>
                    {
                        opts.EnableSending = enableWolverine;
                    });

                    if (instanceRepository != null)
                    {
                        services.AddSingleton(instanceRepository);
                    }
                    else
                    {
                        var internalInstanceRepositoryMock = new InstanceRepositoryMock();

                        Mock<IInstanceRepository> instanceRepositoryMock = new();
                        instanceRepositoryMock
                            .Setup(r =>
                                r.GetOne(_instanceGuid, false, It.IsAny<CancellationToken>())
                            )
                            .Returns(
                                async (
                                    Guid instanceGuid,
                                    bool includeElements,
                                    CancellationToken cancellationToken
                                ) =>
                                {
                                    var (instance, _) = await internalInstanceRepositoryMock.GetOne(
                                        _instanceGuid,
                                        false,
                                        cancellationToken
                                    );
                                    return (instance, _instanceInternalId);
                                }
                            );

                        instanceRepositoryMock
                            .Setup(r =>
                                r.GetOne(
                                    It.IsAny<Guid>(),
                                    It.IsAny<bool>(),
                                    It.IsAny<CancellationToken>()
                                )
                            )
                            .Returns(
                                async (
                                    Guid instanceGuid,
                                    bool includeElements,
                                    CancellationToken cancellationToken
                                ) =>
                                {
                                    var (instance, _) = await internalInstanceRepositoryMock.GetOne(
                                        instanceGuid,
                                        includeElements,
                                        cancellationToken
                                    );
                                    return (instance, _instanceInternalId);
                                }
                            );
                        services.AddSingleton(instanceRepositoryMock.Object);
                    }

                    if (instanceAndEventsRepository != null)
                    {
                        services.AddSingleton(instanceAndEventsRepository);
                    }
                    else
                    {
                        services.AddSingleton<
                            IInstanceAndEventsRepository,
                            InstanceAndEventsRepositoryMock
                        >();
                    }

                    if (instanceLockRepository != null)
                    {
                        services.AddSingleton(instanceLockRepository);
                    }
                });
            })
            .CreateClient();

        return client;
    }
}
