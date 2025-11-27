using System;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.SystemUser;

public class PlatformUserTests
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        PlatformUser target = TestdataHelper.LoadDataFromEmbeddedResourceAsType<PlatformUser>(
            "SystemUser.platformUser_beforeChange.json"
        );

        Assert.Null(target.SystemUserId);
        Assert.Null(target.SystemUserName);
        Assert.Null(target.SystemUserOwnerOrgNo);
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        PlatformUser target = TestdataHelper.LoadDataFromEmbeddedResourceAsType<PlatformUser>(
            "SystemUser.platformUser_afterChange.json"
        );

        Assert.Equal(Guid.Parse("2280457B-0A79-49C5-AC14-09217705C9A1"), target.SystemUserId);
        Assert.Equal("Vismalise", target.SystemUserName);
        Assert.Equal("565433454", target.SystemUserOwnerOrgNo);
    }
}
