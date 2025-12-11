using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.AllowUserActions;

public class AllowUserActions
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "AllowUserActions.applicationMetadata_beforeChange.json"
            );

        Assert.False(
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserCreate
        );
        Assert.False(
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserDelete
        );
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "AllowUserActions.applicationMetadata_afterChange.json"
            );

        Assert.True(
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserCreate
        );
        Assert.False(
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.DisallowUserDelete
        );
    }
}
