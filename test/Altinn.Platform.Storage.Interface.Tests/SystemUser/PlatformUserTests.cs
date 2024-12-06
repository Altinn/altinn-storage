using System;

using Altinn.Platform.Storage.Interface.Models;

using FluentAssertions;

using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.SystemUser;

public class PlatformUserTests
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        PlatformUser target = TestdataHelper.LoadDataFromEmbeddedResourceAsType<PlatformUser>("SystemUser.platformUser_beforeChange.json");

        target.SystemUserId.Should().BeNull();
        target.SystemUserName.Should().BeNull();
        target.SystemUserOwnerOrgNo.Should().BeNull();
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        PlatformUser target = TestdataHelper.LoadDataFromEmbeddedResourceAsType<PlatformUser>("SystemUser.platformUser_afterChange.json");

        target.SystemUserId.Should().Be(Guid.Parse("2280457B-0A79-49C5-AC14-09217705C9A1"));
        target.SystemUserName.Should().Be("Vismalise");
        target.SystemUserOwnerOrgNo.Should().Be("565433454");
    }
}
