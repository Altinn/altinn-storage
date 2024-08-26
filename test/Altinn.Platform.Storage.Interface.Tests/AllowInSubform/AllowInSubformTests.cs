using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using FluentAssertions;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.AllowUserActions;

public class AllowInSubformTests
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("AllowInSubform.applicationMetadata_beforeChange.json");

        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.AllowInSubform.Should().BeFalse();
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("AllowInSubform.applicationMetadata_afterChange.json");

        applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.AllowInSubform.Should().BeTrue();
    }
}
