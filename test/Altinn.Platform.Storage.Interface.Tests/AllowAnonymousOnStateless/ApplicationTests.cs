using System.Linq;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests;

public class ApplicationTests
{
    [Fact]
    public void MetadataWithoutAllowAnonymous_ShouldBeFalse()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "AllowAnonymousOnStateless.applicationMetadata_beforeChange.json"
            );

        Assert.False(
            applicationBefore
                .DataTypes.First(d => d.Id == "Veileder")
                .AppLogic.AllowAnonymousOnStateless
        );
    }

    [Fact]
    public void MetadataWithAllowAnonymous_ShouldBeTrue()
    {
        Application applicationBefore =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                "AllowAnonymousOnStateless.applicationMetadata_afterChange.json"
            );

        Assert.True(
            applicationBefore
                .DataTypes.First(d => d.Id == "Veileder")
                .AppLogic.AllowAnonymousOnStateless
        );
    }
}
