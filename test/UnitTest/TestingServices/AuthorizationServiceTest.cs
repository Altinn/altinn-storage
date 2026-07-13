#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.PEP.Configuration;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Mocks;
using AltinnCore.Authentication.Constants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class AuthorizationServiceTest
{
    private const string Org = "tdd";
    private const string App = "test-applikasjon-1";
    private const string UrnName = "urn:name";
    private const string UrnAuthLv = "urn:altinn:authlevel";
    private const string UrnUserId = "urn:altinn:userid";

    private readonly AuthorizationService _authzService;
    private readonly IPDP _pdpMockSI;
    private readonly Mock<IPDP> _pdpSimpleMock;
    private readonly Mock<IInstanceRepository> _instanceRepository = new();
    private readonly Mock<IClaimsPrincipalProvider> _claimsPrincipalProviderMock = new();

    public AuthorizationServiceTest()
    {
        _pdpSimpleMock = new Mock<IPDP>();
        _pdpMockSI = new PepWithPDPAuthorizationMockSI(_instanceRepository.Object);
        var generalSettings = new GeneralSettings { AuthorizeA2ListInstancesDelete = true };
        var options = Options.Create(generalSettings);
        _authzService = new AuthorizationService(
            _pdpMockSI,
            _claimsPrincipalProviderMock.Object,
            Mock.Of<ILogger<AuthorizationService>>(),
            options,
            Mock.Of<IMemoryCache>(),
            Options.Create(new PepSettings())
        );
    }

    [Fact]
    public async Task GetDecisionForRequest_ConfirmPDPCalled()
    {
        var res = new XacmlJsonResponse
        {
            Response = new List<XacmlJsonResult>() { new XacmlJsonResult { Decision = "Permit" } },
        };

        _pdpSimpleMock
            .Setup(pdp => pdp.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
            .ReturnsAsync(res);

        var generalSettings = new GeneralSettings { AuthorizeA2ListInstancesDelete = true };
        var options = Options.Create(generalSettings);

        var sut = new AuthorizationService(
            _pdpSimpleMock.Object,
            _claimsPrincipalProviderMock.Object,
            Mock.Of<ILogger<AuthorizationService>>(),
            options,
            Mock.Of<IMemoryCache>(),
            Options.Create(new PepSettings())
        );
        await sut.GetDecisionForRequest(new XacmlJsonRequestRoot());

        _pdpSimpleMock.Verify(
            m => m.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()),
            Times.Once()
        );
    }

    [Fact]
    public void UserHasRequiredScope_CaseIgnored_ReturnsTrue()
    {
        // Arrange
        string reqiured = "altinn:serviceowner/instances.read";

        var claims = new List<Claim>();
        claims.Add(
            new Claim(
                "urn:altinn:scope",
                "ALTINN:SERVICEOWNER/INSTANCES.READ",
                ClaimValueTypes.String,
                "maskinporten"
            )
        );

        var identity = new ClaimsIdentity("AuthenticationTypes.Federation");
        identity.AddClaims(claims);
        var principal = new ClaimsPrincipal(identity);
        _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(principal);

        // Act
        var actual = _authzService.UserHasRequiredScope(new List<string> { reqiured });

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void UserHasRequiredScope_MissingRequiredScope_ReturnsFalse()
    {
        // Arrange
        string reqiured = "altinn:serviceowner/instances.read";

        var claims = new List<Claim>();
        string issuer = "www.altinn.no";
        claims.Add(new Claim("urn:altinn:org", "nav", ClaimValueTypes.String, issuer));
        claims.Add(
            new Claim("urn:altinn:orgNumber", "123456789", ClaimValueTypes.Integer32, issuer)
        );
        claims.Add(
            new Claim(
                AltinnCoreClaimTypes.AuthenticateMethod,
                "Mock",
                ClaimValueTypes.String,
                issuer
            )
        );
        claims.Add(
            new Claim(
                AltinnCoreClaimTypes.AuthenticationLevel,
                "3",
                ClaimValueTypes.Integer32,
                issuer
            )
        );
        claims.Add(
            new Claim(
                "urn:altinn:scope",
                "altinn:random.scope",
                ClaimValueTypes.String,
                "maskinporten"
            )
        );

        var identity = new ClaimsIdentity("AuthenticationTypes.Federation");
        identity.AddClaims(claims);
        var principal = new ClaimsPrincipal(identity);

        _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(principal);

        // Act
        var actual = _authzService.UserHasRequiredScope(new List<string> { reqiured });

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Test case: Send attributes and creates multiple request out of it
    /// Expected: All values sent in will be created to attributes
    /// </summary>
    [Fact]
    public void CreateXacmlJsonMultipleRequest_TC01()
    {
        // Arrange
        List<string> actionTypes = new List<string> { "read", "write" };
        List<InstanceInternal> instances = CreateInstances();

        // Act
        XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(
            CreateUserClaims(1),
            instances,
            actionTypes
        );

        // Assert
        // Checks it has the right number of attributes in each category
        Assert.Single(requestRoot.Request.AccessSubject);
        Assert.Equal(2, requestRoot.Request.Action.Count);
        Assert.Equal(3, requestRoot.Request.Resource.Count);
        Assert.Equal(4, requestRoot.Request.Resource.First().Attribute.Count);
        Assert.Equal(6, requestRoot.Request.MultiRequests.RequestReference.Count);

        foreach (var referenceId in requestRoot.Request.MultiRequests.RequestReference)
        {
            Assert.Equal(3, referenceId.ReferenceId.Count);
        }
    }

    /// <summary>
    /// Test case: Send in user with claims that is null
    /// Expected: throws ArgumentNullException
    /// </summary>
    [Fact]
    public void CreateXacmlJsonMultipleRequest_TC02()
    {
        // Arrange
        List<string> actionTypes = new List<string> { "read", "write" };
        List<InstanceInternal> instances = CreateInstances();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            AuthorizationService.CreateMultiDecisionRequest(null, instances, actionTypes)
        );
    }

    /// <summary>
    /// Test case: Migrated A2 instances
    /// Expected: Dummy end events are added
    /// </summary>
    [Fact]
    public void CreateXacmlJsonMultipleRequest_TC03()
    {
        // Arrange
        List<string> actionTypes = new List<string> { "read", "write" };
        List<InstanceInternal> instances = CreateInstances();
        foreach (InstanceInternal instance in instances)
        {
            // Add data values to the instances
            instance.DataValues = new() { { "A2ArchRef", "test" } };
        }

        // Act
        XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(
            CreateUserClaims(1),
            instances,
            actionTypes
        );

        // Assert
        requestRoot.Request.Resource.ForEach(resource =>
        {
            Assert.Contains(
                resource.Attribute,
                attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2"
            );
        });
    }

    /// <summary>
    /// Test case: Migrated A1 instances
    /// Expected: Dummy end events are added
    /// </summary>
    [Fact]
    public void CreateXacmlJsonMultipleRequest_TC04()
    {
        // Arrange
        List<string> actionTypes = new List<string> { "read", "write" };
        List<InstanceInternal> instances = CreateInstances();
        foreach (InstanceInternal instance in instances)
        {
            // Add data values to the instances
            instance.DataValues = new() { { "A1ArchRef", "test" } };
        }

        // Act
        XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(
            CreateUserClaims(1),
            instances,
            actionTypes
        );

        // Assert
        requestRoot.Request.Resource.ForEach(resource =>
        {
            Assert.Contains(
                resource.Attribute,
                attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2"
            );
        });
    }

    /// <summary>
    /// Test case: Normal A3 instances
    /// Expected: Dummy end events are not added
    /// </summary>
    [Fact]
    public void CreateXacmlJsonMultipleRequest_TC05()
    {
        // Arrange
        List<string> actionTypes = new List<string> { "read", "write" };
        List<InstanceInternal> instances = CreateInstances();
        foreach (InstanceInternal instance in instances)
        {
            // Add data values to the instances
            instance.DataValues = new() { { "SomeValue", "test" } };
        }

        // Act
        XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(
            CreateUserClaims(1),
            instances,
            actionTypes
        );

        // Assert
        requestRoot.Request.Resource.ForEach(resource =>
        {
            Assert.DoesNotContain(
                resource.Attribute,
                attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2"
            );
        });
    }

    /// <summary>
    /// Test case: Authorize an convert emtpy list of instances to messageboxInstances
    /// Expected: An empty list is returned.
    /// </summary>
    [Fact]
    public async Task AuthorizeMesseageBoxInstances_TC01_EmptyList()
    {
        // Arrange
        List<MessageBoxInstance> expected = new List<MessageBoxInstance>();
        List<InstanceInternal> instances = [];
        _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(CreateUserClaims(3));

        // Act
        List<MessageBoxInstance> actual = await _authzService.AuthorizeMesseageBoxInstances(
            instances,
            false
        );

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task AuthorizeMessageBoxInstances_DomainBatch_UsesCompositeIdsAndSortedPermitResults()
    {
        InstanceInternal second = CreateMessageBoxDomainInstance(
            "B45EA5DB-6DD4-4476-B774-BDB2A09DA7EA"
        );
        InstanceInternal first = CreateMessageBoxDomainInstance(
            "A45EA5DB-6DD4-4476-B774-BDB2A09DA7EA"
        );
        InstanceInternal denied = CreateMessageBoxDomainInstance(
            "C45EA5DB-6DD4-4476-B774-BDB2A09DA7EA"
        );
        XacmlJsonResponse response = new()
        {
            Response =
            [
                CreateInstanceDecision($"1000/{second.Id}", "Permit"),
                CreateInstanceDecision($"1000/{denied.Id}", "Deny"),
                CreateInstanceDecision($"1000/{first.Id}", "Permit"),
            ],
        };
        List<XacmlJsonRequestRoot> requests = [];
        AuthorizationService service = CreateRequestCapturingService(requests, response: response);

        List<MessageBoxInstance> authorized = await service.AuthorizeMesseageBoxInstances(
            [second, first, denied],
            false
        );

        XacmlJsonRequestRoot request = Assert.Single(requests);
        Assert.Equal(3, request.Request.Resource.Count);
        Assert.Equal(6, request.Request.MultiRequests.RequestReference.Count);
        Assert.Equal(
            [$"1000/{second.Id}", $"1000/{first.Id}", $"1000/{denied.Id}"],
            request
                .Request.Resource.SelectMany(category => category.Attribute)
                .Where(attribute => attribute.AttributeId == "urn:altinn:instance-id")
                .Select(attribute => attribute.Value)
        );
        Assert.Equal([first.Id, second.Id], authorized.Select(instance => instance.Id));
        Assert.DoesNotContain(authorized, instance => instance.Id == denied.Id);
    }

    [Fact]
    public async Task AuthorizeInstances_DomainInput_UsesCompositeInstanceId()
    {
        const string storageId = "A45EA5DB-6DD4-4476-B774-BDB2A09DA7EA";
        InstanceInternal instance = CreateDomainInstance(storageId);
        List<XacmlJsonRequestRoot> requests = [];

        await CreateRequestCapturingService(requests).AuthorizeInstances([instance]);

        Assert.Contains(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute =>
                attribute.AttributeId == "urn:altinn:instance-id"
                && attribute.Value == $"1000/{storageId}"
        );
    }

    [Fact]
    public async Task AuthorizeInstances_DomainList_ReturnsOnlyPermittedInstancesInDecisionOrder()
    {
        InstanceInternal first = CreateDomainInstance("045ea5db-6dd4-4476-b774-bdb2a09da7ea");
        InstanceInternal second = CreateDomainInstance("145ea5db-6dd4-4476-b774-bdb2a09da7ea");
        InstanceInternal denied = CreateDomainInstance("245ea5db-6dd4-4476-b774-bdb2a09da7ea");
        InstanceInternal omitted = CreateDomainInstance("345ea5db-6dd4-4476-b774-bdb2a09da7ea");
        XacmlJsonResponse response = new()
        {
            Response =
            [
                CreateInstanceDecision("1000/145ea5db-6dd4-4476-b774-bdb2a09da7ea", "Permit"),
                CreateInstanceDecision("1000/245ea5db-6dd4-4476-b774-bdb2a09da7ea", "Deny"),
                CreateInstanceDecision("1000/045ea5db-6dd4-4476-b774-bdb2a09da7ea", "Permit"),
            ],
        };
        AuthorizationService service = CreateRequestCapturingService([], response: response);

        List<InstanceInternal> authorized = await service.AuthorizeInstances([
            first,
            second,
            denied,
            omitted,
        ]);

        Assert.Equal([second, first], authorized);
        Assert.DoesNotContain(omitted, authorized);
    }

    [Fact]
    public async Task AuthorizeInstanceAction_DomainInput_IncludesExplicitTask()
    {
        InstanceInternal instance = CreateDomainInstance();
        List<XacmlJsonRequestRoot> requests = [];
        AuthorizationService service = CreateRequestCapturingService(requests);

        await service.AuthorizeInstanceAction(instance, "read", "Task_Override");

        Assert.Contains(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute => attribute.AttributeId == "urn:altinn:task"
        );
    }

    [Fact]
    public async Task AuthorizeInstanceAction_NullId_OmitsTask()
    {
        InstanceInternal instance = CreateDomainInstance();
        instance.Id = null;
        List<XacmlJsonRequestRoot> requests = [];

        await CreateRequestCapturingService(requests)
            .AuthorizeInstanceAction(instance, "read", "Task_Must_Not_Be_Included");

        Assert.DoesNotContain(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute => attribute.AttributeId == "urn:altinn:task"
        );
    }

    [Fact]
    public async Task AuthorizeEnrichedInstanceAction_ProcessEnd_IncludesEndEvent()
    {
        InstanceInternal instance = CreateDomainInstance();
        instance.Process.CurrentTask = null;
        instance.Process.EndEvent = "EndEvent_1";
        List<XacmlJsonRequestRoot> requests = [];

        await CreateRequestCapturingService(requests)
            .AuthorizeEnrichedInstanceAction(instance, "read");

        Assert.Contains(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute =>
                attribute.AttributeId == "urn:altinn:end-event" && attribute.Value == "EndEvent_1"
        );
    }

    [Fact]
    public async Task AuthorizeEnrichedInstanceAction_MigratedDataValues_PreservesApprovedRequestShape()
    {
        InstanceInternal instance = CreateDomainInstance();
        instance.Process = null;
        instance.DataValues = new Dictionary<string, string> { ["A2ArchRef"] = "12345" };
        List<XacmlJsonRequestRoot> requests = [];

        await CreateRequestCapturingService(requests)
            .AuthorizeEnrichedInstanceAction(instance, "read");

        Assert.DoesNotContain(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute =>
                attribute.AttributeId == "urn:altinn:end-event" && attribute.Value == "MigratedA1A2"
        );
    }

    [Fact]
    public async Task AuthorizeEnrichedInstanceAction_EquivalentInputsShareCacheEntry()
    {
        InstanceInternal first = CreateDomainInstance();
        InstanceInternal second = CreateDomainInstance();
        List<XacmlJsonRequestRoot> requests = [];
        Mock<IPDP> pdp = new();
        using MemoryCache cache = new(new MemoryCacheOptions());
        AuthorizationService service = CreateRequestCapturingService(requests, pdp, cache);

        await service.AuthorizeEnrichedInstanceAction(first, "read");
        await service.AuthorizeEnrichedInstanceAction(second, "read");

        Assert.Single(requests);
        pdp.Verify(
            instance => instance.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()),
            Times.Once
        );
    }

    [Fact]
    public async Task AuthorizeAnyOfInstanceActions_MigratedDataValues_PreservesApprovedRequestShape()
    {
        InstanceInternal instance = CreateDomainInstance();
        instance.Process = null;
        instance.DataValues = new Dictionary<string, string> { ["A2ArchRef"] = "12345" };
        List<XacmlJsonRequestRoot> requests = [];

        await CreateRequestCapturingService(requests)
            .AuthorizeAnyOfInstanceActions(instance, ["read", "write"]);

        Assert.Equal(2, requests[0].Request.Action.Count);
        Assert.Contains(
            requests[0].Request.Resource.SelectMany(category => category.Attribute),
            attribute =>
                attribute.AttributeId == "urn:altinn:end-event" && attribute.Value == "MigratedA1A2"
        );
    }

    [Fact]
    public async Task AuthorizeAnyOfInstanceActions_EmptyActionsAndNullInstance_ReturnsFalseWithoutPdp()
    {
        List<XacmlJsonRequestRoot> requests = [];
        Mock<IPDP> pdp = new();
        AuthorizationService service = CreateRequestCapturingService(requests, pdp);

        bool decision = await service.AuthorizeAnyOfInstanceActions((InstanceInternal)null, []);

        Assert.False(decision);
        Assert.Empty(requests);
        pdp.Verify(
            instance => instance.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()),
            Times.Never
        );
    }

    private AuthorizationService CreateRequestCapturingService(
        List<XacmlJsonRequestRoot> requests,
        Mock<IPDP> pdp = null,
        IMemoryCache memoryCache = null,
        XacmlJsonResponse response = null
    )
    {
        pdp ??= new Mock<IPDP>();
        pdp.Setup(instance => instance.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
            .Callback<XacmlJsonRequestRoot>(requests.Add)
            .ReturnsAsync(response ?? new XacmlJsonResponse { Response = [] });
        _claimsPrincipalProviderMock
            .Setup(provider => provider.GetUser())
            .Returns(CreateUserClaims(1));

        return new AuthorizationService(
            pdp.Object,
            _claimsPrincipalProviderMock.Object,
            Mock.Of<ILogger<AuthorizationService>>(),
            Options.Create(new GeneralSettings()),
            memoryCache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new PepSettings { PdpDecisionCachingTimeout = 5 })
        );
    }

    private static InstanceInternal CreateDomainInstance(
        string instanceGuid = "045ea5db-6dd4-4476-b774-bdb2a09da7ea"
    )
    {
        return new InstanceInternal
        {
            Id = instanceGuid,
            InstanceOwner = new InstanceOwner { PartyId = "1000" },
            AppId = $"{Org}/{App}",
            Org = Org,
            Process = new ProcessState
            {
                CurrentTask = new ProcessElementInfo { ElementId = "Task_1" },
            },
            DataValues = new Dictionary<string, string> { ["case"] = "value" },
        };
    }

    private static InstanceInternal CreateMessageBoxDomainInstance(string instanceGuid)
    {
        InstanceInternal instance = CreateDomainInstance(instanceGuid);
        instance.Status = new InstanceStatus();
        instance.Created = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        instance.LastChanged = instance.Created;
        instance.LastChangedBy = "1000";
        instance.Data = [];
        return instance;
    }

    private static XacmlJsonResult CreateInstanceDecision(string instanceId, string decision)
    {
        return new XacmlJsonResult
        {
            Decision = decision,
            Category =
            [
                new XacmlJsonCategory
                {
                    Attribute =
                    [
                        new XacmlJsonAttribute
                        {
                            AttributeId = "urn:altinn:instance-id",
                            Value = instanceId,
                        },
                    ],
                },
            ],
        };
    }

    private static ClaimsPrincipal CreateUserClaims(int userId)
    {
        // Create the user
        List<Claim> claims = new()
        {
            // type, value, valuetype, issuer
            new Claim(UrnName, "Ola", "string", "org"),
            new Claim(UrnAuthLv, "2", "string", "org"),
            new Claim(UrnUserId, $"{userId}", "string", "org"),
        };

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        return user;
    }

    private static List<InstanceInternal> CreateInstances()
    {
        List<InstanceInternal> instances = new List<InstanceInternal>
        {
            new InstanceInternal
            {
                Id = Guid.NewGuid().ToString(),
                Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo { Name = "test_task" },
                },
                InstanceOwner = new InstanceOwner { PartyId = "1000" },
                AppId = Org + "/" + App,
                Org = Org,
                Created = DateTime.UtcNow,
            },
            new InstanceInternal
            {
                Id = Guid.NewGuid().ToString(),
                InstanceOwner = new InstanceOwner { PartyId = "1002" },
                AppId = Org + "/" + App,
                Org = Org,
                Created = DateTime.UtcNow,
            },
            new InstanceInternal
            {
                Id = Guid.NewGuid().ToString(),
                InstanceOwner = new InstanceOwner { PartyId = "1000" },
                AppId = Org + "/" + App,
                Org = Org,
                Created = DateTime.UtcNow,
            },
        };

        return instances;
    }
}
