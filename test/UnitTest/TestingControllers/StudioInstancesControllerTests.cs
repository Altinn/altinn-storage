#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.AspNetCore.Mvc;
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
        Guid storageId = new("01234567-89ab-cdef-0123-456789abcdef");
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir =>
                ir.GetInstancesFromQuery(
                    It.Is<InstanceQueryParameters>(parameters =>
                        parameters.AppId == "ttd/app" && !parameters.IncludeDataElements
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceQueryResult
                {
                    Instances = new List<InstanceInternal>
                    {
                        new InstanceInternal
                        {
                            Id = storageId,
                            InstanceOwner = new() { PartyId = "1337" },
                            AppId = "ttd/app",
                            Org = "ttd",
                            Status = new()
                            {
                                ReadStatus = ReadStatus.UpdatedSinceLastReview,
                                Archived = new DateTime(2026, 1, 5, 6, 7, 8, DateTimeKind.Utc),
                            },
                            Process = new()
                            {
                                CurrentTask = new() { ElementId = "Task_1", Name = "Review" },
                                Ended = new DateTime(2026, 1, 4, 5, 6, 7, DateTimeKind.Utc),
                            },
                            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                            LastChanged = new DateTime(2026, 1, 3, 4, 5, 6, DateTimeKind.Utc),
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
        Assert.Equal(
            $"{{\"count\":1,\"self\":null,\"next\":null,\"instances\":[{{\"id\":\"{storageId}\",\"org\":\"ttd\",\"app\":\"app\",\"isRead\":true,\"currentTaskId\":\"Task_1\",\"currentTaskName\":\"Review\",\"completedAt\":\"2026-01-04T05:06:07+00:00\",\"archivedAt\":\"2026-01-05T06:07:08+00:00\",\"softDeletedAt\":null,\"hardDeletedAt\":null,\"confirmedAt\":null,\"createdAt\":\"2026-01-02T03:04:05+00:00\",\"lastChangedAt\":\"2026-01-03T04:05:06+00:00\"}}]}}",
            content
        );
        instanceRepositoryMock.VerifyAll();
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
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new InstanceQueryResult { Exception = "Something went wrong" });

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
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new InstanceQueryResult { Instances = [], ContinuationToken = "nextToken" }
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
    public async Task GetInstances_ForwardsFiltersPagingAndCancellation_AndPreservesRepositoryOrder()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        InstanceQueryParameters capturedParameters = null;
        CancellationToken capturedCancellationToken = default;
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<InstanceQueryParameters, CancellationToken>(
                (parameters, cancellationToken) =>
                {
                    capturedParameters = parameters;
                    capturedCancellationToken = cancellationToken;
                }
            )
            .ReturnsAsync(
                new InstanceQueryResult
                {
                    ContinuationToken = "next/token",
                    Instances =
                    [
                        CreateListInstance(new("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")),
                        CreateListInstance(new("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")),
                    ],
                }
            );
        StudioInstancesController controller = new(
            instanceRepositoryMock.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<StudioInstancesController>>(),
            Mock.Of<IApplicationService>(),
            Mock.Of<IInstanceEventService>(),
            Mock.Of<IOrganisationService>()
        );
        StudioInstanceParameters parameters = new()
        {
            Org = "ttd",
            App = "app",
            ArchiveReference = "archive-ref",
            ProcessCurrentTask = "Task_1",
            ProcessIsComplete = false,
            LastChanged = ["gt:2026-01-01", "lt:2026-02-01"],
            Created = ["gt:2025-01-01"],
            Confirmed = false,
            IsSoftDeleted = true,
            IsHardDeleted = false,
            IsArchived = true,
            ContinuationToken = "current%2Ftoken",
            Size = 25,
        };

        ActionResult<QueryResponse<SimpleInstance>> actionResult = await controller.GetInstances(
            parameters,
            cancellationTokenSource.Token
        );

        OkObjectResult okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        QueryResponse<SimpleInstance> response = Assert.IsType<QueryResponse<SimpleInstance>>(
            okResult.Value
        );
        Assert.Equal(
            ["bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"],
            response.Instances.Select(instance => instance.Id)
        );
        Assert.Equal("next%2ftoken", response.Next);
        Assert.Equal(cancellationTokenSource.Token, capturedCancellationToken);
        Assert.Equal("ttd/app", capturedParameters.AppId);
        Assert.Equal("archive-ref", capturedParameters.ArchiveReference);
        Assert.Equal("Task_1", capturedParameters.ProcessCurrentTask);
        Assert.False(capturedParameters.ProcessIsComplete);
        Assert.Equal(["gt:2026-01-01", "lt:2026-02-01"], capturedParameters.LastChanged);
        Assert.Equal(["gt:2025-01-01"], capturedParameters.Created);
        Assert.False(capturedParameters.Confirmed);
        Assert.True(capturedParameters.IsSoftDeleted);
        Assert.False(capturedParameters.IsHardDeleted);
        Assert.True(capturedParameters.IsArchived);
        Assert.Equal("current/token", capturedParameters.ContinuationToken);
        Assert.Equal(25, capturedParameters.Size);
        Assert.Equal(3, capturedParameters.MainVersionInclude);
        Assert.False(capturedParameters.IncludeDataElements);
    }

    [Fact]
    public async Task GetInstances_CancelledRepositoryResult_Returns499()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        Mock<IInstanceRepository> instanceRepositoryMock = new();
        instanceRepositoryMock
            .Setup(repository =>
                repository.GetInstancesFromQuery(
                    It.IsAny<InstanceQueryParameters>(),
                    cancellationTokenSource.Token
                )
            )
            .ReturnsAsync(new InstanceQueryResult { Exception = "The query was canceled." });
        StudioInstancesController controller = new(
            instanceRepositoryMock.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<StudioInstancesController>>(),
            Mock.Of<IApplicationService>(),
            Mock.Of<IInstanceEventService>(),
            Mock.Of<IOrganisationService>()
        );

        ActionResult<QueryResponse<SimpleInstance>> actionResult = await controller.GetInstances(
            new StudioInstanceParameters { Org = "ttd", App = "app" },
            cancellationTokenSource.Token
        );

        ObjectResult result = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(499, result.StatusCode);
        Assert.Equal("The query was canceled.", result.Value);
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
        var instanceGuid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var instance = new InstanceInternal
        {
            Id = instanceGuid,
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
            Status = new()
            {
                ReadStatus = ReadStatus.UpdatedSinceLastReview,
                Archived = new DateTime(2026, 1, 5, 6, 7, 8, DateTimeKind.Utc),
                SoftDeleted = new DateTime(2026, 1, 6, 7, 8, 9, DateTimeKind.Utc),
                HardDeleted = new DateTime(2026, 1, 7, 8, 9, 10, DateTimeKind.Utc),
            },
            Process = new()
            {
                CurrentTask = new() { ElementId = "Task_1", Name = "Review" },
                Ended = new DateTime(2026, 1, 4, 5, 6, 7, DateTimeKind.Utc),
            },
            CompleteConfirmations =
            [
                new()
                {
                    StakeholderId = "later",
                    ConfirmedOn = new DateTime(2026, 1, 9, 10, 11, 12, DateTimeKind.Utc),
                },
                new()
                {
                    StakeholderId = "first",
                    ConfirmedOn = new DateTime(2026, 1, 8, 9, 10, 11, DateTimeKind.Utc),
                },
            ],
            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            LastChanged = new DateTime(2026, 1, 3, 4, 5, 6, DateTimeKind.Utc),
            Data =
            [
                new()
                {
                    Id = new Guid("11111111-2222-3333-4444-555555555555"),
                    DataType = "main",
                    ContentType = "application/json",
                    Size = 123,
                    Locked = true,
                    IsRead = false,
                    FileScanResult = FileScanResult.Clean,
                    DeleteStatus = new()
                    {
                        HardDeleted = new DateTime(2026, 1, 12, 13, 14, 15, DateTimeKind.Utc),
                    },
                    Created = new DateTime(2026, 1, 10, 11, 12, 13, DateTimeKind.Utc),
                    LastChanged = new DateTime(2026, 1, 11, 12, 13, 14, DateTimeKind.Utc),
                },
            ],
        };

        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        // Act
        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app/{instanceGuid}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            "{\"data\":[{\"id\":\"11111111-2222-3333-4444-555555555555\",\"dataType\":\"main\",\"contentType\":\"application/json\",\"size\":123,\"locked\":true,\"isRead\":false,\"fileScanResult\":\"Clean\",\"hardDeletedAt\":\"2026-01-12T13:14:15+00:00\",\"createdAt\":\"2026-01-10T11:12:13+00:00\",\"lastChangedAt\":\"2026-01-11T12:13:14+00:00\"}],\"id\":\"01234567-89ab-cdef-0123-456789abcdef\",\"org\":\"ttd\",\"app\":\"app\",\"isRead\":true,\"currentTaskId\":\"Task_1\",\"currentTaskName\":\"Review\",\"completedAt\":\"2026-01-04T05:06:07+00:00\",\"archivedAt\":\"2026-01-05T06:07:08+00:00\",\"softDeletedAt\":\"2026-01-06T07:08:09+00:00\",\"hardDeletedAt\":\"2026-01-07T08:09:10+00:00\",\"confirmedAt\":\"2026-01-08T09:10:11+00:00\",\"createdAt\":\"2026-01-02T03:04:05+00:00\",\"lastChangedAt\":\"2026-01-03T04:05:06+00:00\"}",
            content
        );
    }

    [Fact]
    public async Task GetSingleInstance_EmptyDataAndDefaults_ReturnsExactJson()
    {
        var instanceGuid = Guid.Parse("31234567-89ab-cdef-0123-456789abcdef");
        var instance = new InstanceInternal
        {
            Id = instanceGuid,
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
            Data = [],
        };
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(instanceGuid, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object
        );

        HttpResponseMessage response = await client.GetAsync($"{BasePath}/ttd/app/{instanceGuid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"data\":[],\"id\":\"31234567-89ab-cdef-0123-456789abcdef\",\"org\":\"ttd\",\"app\":\"app\",\"isRead\":false,\"currentTaskId\":null,\"currentTaskName\":null,\"completedAt\":null,\"archivedAt\":null,\"softDeletedAt\":null,\"hardDeletedAt\":null,\"confirmedAt\":null,\"createdAt\":null,\"lastChangedAt\":null}",
            await response.Content.ReadAsStringAsync()
        );
    }

    [Theory]
    [InlineData(
        null,
        "ttd",
        "ttd/app",
        "Instance 51234567-89ab-cdef-0123-456789abcdef is missing InstanceOwner.PartyId."
    )]
    [InlineData(
        "1337",
        null,
        "ttd/app",
        "Instance 51234567-89ab-cdef-0123-456789abcdef is missing Org/AppId."
    )]
    [InlineData(
        "1337",
        "ttd",
        null,
        "Instance 51234567-89ab-cdef-0123-456789abcdef is missing Org/AppId."
    )]
    [InlineData(
        "1337",
        "ttd",
        "other/app",
        "App id other/app has an unexpected format, expected '{org}/{app}'."
    )]
    public void SimpleInstanceFromInstance_InvalidOutputInvariants_Throws(
        string partyId,
        string org,
        string appId,
        string expectedMessage
    )
    {
        var instance = new InstanceInternal
        {
            Id = new Guid("51234567-89ab-cdef-0123-456789abcdef"),
            InstanceOwner = new() { PartyId = partyId },
            Org = org,
            AppId = appId,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            SimpleInstance.FromInstance(instance)
        );

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task GetSingleInstance_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceInternal)null);

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

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
    public async Task DeleteInstance_AppMismatch_ReturnsNotFound()
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
            .Setup(ir => ir.GetOne(instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            instanceEventService: instanceEventServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        instanceRepositoryMock.Verify(
            ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Never
        );
        instanceEventServiceMock.Verify(
            s =>
                s.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<PlatformUser>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task DeleteInstance_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var instanceRepositoryMock = new Mock<IInstanceRepository>();
        instanceRepositoryMock
            .Setup(ir => ir.GetOne(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceInternal)null);

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));
        instanceRepositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ThrowsAsync(new Exception("Database connection error"));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        var organisationServiceMock = new Mock<IOrganisationService>();
        organisationServiceMock
            .Setup(s => s.GetOrgNumber("ttd", It.IsAny<CancellationToken>()))
            .ReturnsAsync("991825827");

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object,
            organisationService: organisationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInstance_ProcessStatusConflict_ReturnsConflictWithCurrentStatus()
    {
        const ProcessStatus currentStatus = ProcessStatus.Processing;
        Guid instanceGuid = Guid.NewGuid();
        Instance instance = new()
        {
            Id = $"1337/{instanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };
        Mock<IInstanceRepository> instanceRepository = new();
        instanceRepository
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));
        instanceRepository
            .Setup(repository =>
                repository.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(currentStatus));
        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(service => service.GetApplicationOrErrorAsync(instance.AppId))
            .ReturnsAsync((new Application(), null));
        Mock<IOrganisationService> organisationService = new();
        organisationService
            .Setup(service => service.GetOrgNumber(instance.Org, It.IsAny<CancellationToken>()))
            .ReturnsAsync("991825827");

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepository.Object,
            applicationService: applicationService.Object,
            organisationService: organisationService.Object
        );

        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/{instance.Org}/app/{instanceGuid}"
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            currentStatus.ToString().ToLowerInvariant(),
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal
        );
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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));
        instanceRepositoryMock
            .Setup(ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                )
            )
            .ReturnsAsync(
                (InstanceInternal i, List<string> _, CancellationToken _, int? _, int? _) => i
            );

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var organisationServiceMock = new Mock<IOrganisationService>();
        organisationServiceMock
            .Setup(s => s.GetOrgNumber("ttd", It.IsAny<CancellationToken>()))
            .ReturnsAsync("991825827");

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object,
            instanceEventService: instanceEventServiceMock.Object,
            organisationService: organisationServiceMock.Object
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
                    It.Is<InstanceInternal>(i =>
                        i.Status.IsSoftDeleted == true
                        && i.Status.SoftDeleted != null
                        && i.LastChangedBy == "991825827"
                    ),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Once
        );
        instanceEventServiceMock.Verify(
            s =>
                s.DispatchEvent(
                    InstanceEventType.Deleted,
                    It.IsAny<InstanceInternal>(),
                    It.Is<PlatformUser>(u => u.OrgId == "ttd"),
                    It.IsAny<string>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task DeleteInstance_OrgNumberLookupThrows_Returns500()
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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var organisationServiceMock = new Mock<IOrganisationService>();
        organisationServiceMock
            .Setup(s => s.GetOrgNumber("ttd", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("CDN unavailable"));

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object,
            instanceEventService: instanceEventServiceMock.Object,
            organisationService: organisationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        instanceRepositoryMock.Verify(
            ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Never
        );
        instanceEventServiceMock.Verify(
            s =>
                s.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<PlatformUser>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task DeleteInstance_OrgNumberNotResolved_Returns500()
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
            .ReturnsAsync(InstanceInternalTestFactory.Create(instance, [], InternalId: 1));

        var applicationServiceMock = new Mock<IApplicationService>();
        applicationServiceMock
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application(), null));

        var instanceEventServiceMock = new Mock<IInstanceEventService>();

        var organisationServiceMock = new Mock<IOrganisationService>();
        organisationServiceMock
            .Setup(s => s.GetOrgNumber("ttd", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null);

        HttpClient client = GetAuthenticatedClient(
            instanceRepository: instanceRepositoryMock.Object,
            applicationService: applicationServiceMock.Object,
            instanceEventService: instanceEventServiceMock.Object,
            organisationService: organisationServiceMock.Object
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            $"{BasePath}/ttd/app/{instanceGuid}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        instanceRepositoryMock.Verify(
            ir =>
                ir.Update(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    null
                ),
            Times.Never
        );
        instanceEventServiceMock.Verify(
            s =>
                s.DispatchEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<PlatformUser>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    private HttpClient GetAuthenticatedClient(
        IInstanceRepository instanceRepository = null,
        IApplicationService applicationService = null,
        IInstanceEventService instanceEventService = null,
        IOrganisationService organisationService = null,
        string tokenAppId = "studio.designer"
    )
    {
        HttpClient client = GetTestClient(
            instanceRepository,
            applicationService,
            instanceEventService,
            organisationService
        );
        string token = PrincipalUtil.GetAccessToken(tokenAppId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient GetTestClient(
        IInstanceRepository instanceRepository = null,
        IApplicationService applicationService = null,
        IInstanceEventService instanceEventService = null,
        IOrganisationService organisationService = null
    )
    {
        if (instanceRepository == null)
        {
            instanceRepository = new Mock<IInstanceRepository>().Object;
        }

        organisationService ??= new Mock<IOrganisationService>().Object;

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
                    services.AddSingleton(organisationService);
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

    private static InstanceInternal CreateListInstance(Guid id) =>
        new()
        {
            Id = id,
            InstanceOwner = new() { PartyId = "1337" },
            AppId = "ttd/app",
            Org = "ttd",
        };
}
