using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using FluentAssertions;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.AllowUserActions;

public class AllowUserActions
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("AllowUserActions.applicationMetadata_beforeChange.json");

        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserCreate.Should().BeFalse();
        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserDelete.Should().BeFalse();
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("AllowUserActions.applicationMetadata_afterChange.json");

        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserCreate.Should().BeTrue();
        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserDelete.Should().BeFalse();
    }
}
