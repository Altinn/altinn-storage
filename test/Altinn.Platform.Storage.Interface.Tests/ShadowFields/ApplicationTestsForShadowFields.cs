using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests;

public class ApplicationTestsForShadowFields
{
    [Fact]
    public void MetadataWithoutShadowFields_ShouldBeFalse()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "ShadowFields.applicationMetadata_beforeChange.json"
            );

        Assert.Null(
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.ShadowFields
        );
    }

    [Fact]
    public void MetadataWithShadowFields_ShouldBeTrue()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "ShadowFields.applicationMetadata_afterChange.json"
            );

        Assert.Equal(
            "AltinnSF_",
            applicationBefore.DataTypes.First(d => d.Id == "Veileder").AppLogic.ShadowFields.Prefix
        );
        Assert.Equal(
            "model-clean",
            applicationBefore
                .DataTypes.First(d => d.Id == "Veileder")
                .AppLogic.ShadowFields.SaveToDataType
        );
    }
}
