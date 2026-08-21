#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.TestingRepositories;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

[Collection("StoragePostgreSQL")]
public class InstancesControllerCreationIntegrationTests : IClassFixture<InstanceFixture>
{
    private const int _partyId = 1337;
    private const string _appId = "tdd/endring-av-navn";
    private readonly InstanceFixture _instanceFixture;

    public InstancesControllerCreationIntegrationTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;
        _ = PostgresUtil
            .RunSql(
                "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;"
            )
            .Result;
    }

    [Fact]
    public async Task Post_ProcessingStatus_PersistsExactlyAndBlocksOrdinaryWriteWithoutSideEffects()
    {
        Mock<IAuthorization> authorization = new();
        authorization
            .Setup(service => service.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
            .ReturnsAsync(
                new XacmlJsonResponse { Response = [new XacmlJsonResult { Decision = "Permit" }] }
            );
        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(service => service.GetApplicationOrErrorAsync(_appId))
            .ReturnsAsync((new Application { Id = _appId, Org = "tdd" }, null));
        Mock<IPartiesWithInstancesClient> partiesWithInstancesClient = new();
        partiesWithInstancesClient
            .Setup(client => client.SetHasAltinn3Instances(_partyId))
            .Returns(Task.CompletedTask);
        Mock<IInstanceEventService> instanceEventService = new();
        instanceEventService
            .Setup(service =>
                service.DispatchEvent(InstanceEventType.Created, It.IsAny<InstanceInternal>())
            )
            .Returns(Task.CompletedTask);
        Mock<IProcessAuthorizer> processAuthorizer = new();
        processAuthorizer
            .Setup(authorizer => authorizer.AuthorizeDataValuesUpdate(It.IsAny<InstanceInternal>()))
            .ReturnsAsync(true);
        DefaultHttpContext httpContext = new()
        {
            User = PrincipalUtil.GetPrincipal(3, _partyId, 3),
        };
        InstancesController controller = new(
            _instanceFixture.InstanceRepo,
            partiesWithInstancesClient.Object,
            NullLogger<InstancesController>.Instance,
            authorization.Object,
            instanceEventService.Object,
            Mock.Of<IRegisterService>(),
            applicationService.Object,
            Options.Create(new GeneralSettings { Hostname = "https://storage.test" }),
            processAuthorizer.Object
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        Instance incoming = new()
        {
            InstanceOwner = new InstanceOwner { PartyId = _partyId.ToString() },
            Process = new ProcessState { Status = ProcessStatus.Processing },
            DataValues = new Dictionary<string, string> { ["preserved"] = "value" },
        };

        ActionResult<Instance> createResult = await controller.Post(
            _appId,
            incoming,
            CancellationToken.None
        );

        CreatedResult createdResult = Assert.IsType<CreatedResult>(createResult.Result);
        Instance created = Assert.IsType<Instance>(createdResult.Value);
        Assert.Equal(ProcessStatus.Processing, created.Process.Status);
        Guid instanceGuid = Guid.Parse(created.Id.Split('/')[^1]);
        InstanceInternal persistedBefore = await _instanceFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        Assert.Equal(ProcessStatus.Processing, persistedBefore.Process.Status);
        Assert.Equal("value", persistedBefore.DataValues["preserved"]);
        Assert.Equal("\"processing\"", await ReadStoredProcessStatusRepresentation(instanceGuid));
        StorageVersions versionsBefore = persistedBefore.Versions;
        string rawInstanceBefore = await ReadRawInstance(instanceGuid);

        ActionResult<Instance> updateResult = await controller.UpdateDataValues(
            _partyId,
            instanceGuid,
            new DataValues
            {
                Values = new Dictionary<string, string> { ["blocked"] = "not-applied" },
            },
            CancellationToken.None
        );

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(updateResult.Result);
        Assert.Equal((int)HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains(
            "processing",
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
        InstanceInternal persistedAfter = await _instanceFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        Assert.Equal(ProcessStatus.Processing, persistedAfter.Process.Status);
        Assert.Equal(versionsBefore, persistedAfter.Versions);
        Assert.Equal("value", persistedAfter.DataValues["preserved"]);
        Assert.False(persistedAfter.DataValues.ContainsKey("blocked"));
        Assert.Equal(rawInstanceBefore, await ReadRawInstance(instanceGuid));
        instanceEventService.Verify(
            service =>
                service.DispatchEvent(InstanceEventType.Created, It.IsAny<InstanceInternal>()),
            Times.Once
        );
        partiesWithInstancesClient.VerifyAll();
    }

    private static Task<string> ReadStoredProcessStatusRepresentation(Guid instanceGuid) =>
        PostgresUtil.RunQuery<string>(
            $"select case when instance -> 'Process' ? 'Status' then (instance -> 'Process' -> 'Status')::text else '<absent>' end from storage.instances where alternateid = '{instanceGuid}'"
        );

    private static Task<string> ReadRawInstance(Guid instanceGuid) =>
        PostgresUtil.RunQuery<string>(
            $"select instance::text from storage.instances where alternateid = '{instanceGuid}'"
        );
}
