#nullable disable

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.PEP.Authorization;
using Altinn.Common.PEP.Configuration;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingHandlers;

public class StorageAccessHandlerTests
{
    [Fact]
    public async Task HandleRequirementAsync_PdpDenies_KeepsRequirementPending()
    {
        // Arrange
        AppAccessRequirement requirement = new AppAccessRequirement("read");
        ClaimsPrincipal user = PrincipalUtil.GetPrincipal(1337, 1000);
        Guid instanceGuid = Guid.NewGuid();

        RouteData routeData = new RouteData();
        routeData.Values["org"] = "ttd";
        routeData.Values["app"] = "test-app";
        routeData.Values["instanceOwnerPartyId"] = "1000";
        routeData.Values["instanceGuid"] = instanceGuid.ToString();

        DefaultHttpContext httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = routeData });

        Mock<IHttpContextAccessor> httpContextAccessor = new();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        Mock<IInstanceRepository> instanceRepository = new();
        instanceRepository
            .Setup(r => r.GetOne(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Instance)null, 0));

        XacmlJsonResponse denyResponse = new XacmlJsonResponse
        {
            Response = new List<XacmlJsonResult> { new XacmlJsonResult { Decision = "Deny" } },
        };
        Mock<IPDP> pdp = new();
        pdp.Setup(p => p.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
            .ReturnsAsync(denyResponse);

        Mock<IAuthorization> authorization = new();
        authorization.Setup(a => a.UserHasRequiredScope(It.IsAny<List<string>>())).Returns(false);

        StorageAccessHandler handler = new StorageAccessHandler(
            httpContextAccessor.Object,
            pdp.Object,
            authorization.Object,
            Options.Create(new GeneralSettings { InstanceSyncAdapterScope = "altinn:sync" }),
            Options.Create(new PepSettings()),
            Mock.Of<ILogger<StorageAccessHandler>>(),
            instanceRepository.Object,
            Mock.Of<IMemoryCache>()
        );

        AuthorizationHandlerContext context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement },
            user,
            resource: null
        );

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasFailed);
        Assert.Contains(requirement, context.PendingRequirements);
    }
}
