using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests;

public class CopyInstanceSettingsTests
{
    [Fact]
    public void MetadataWithoutIncludedValues_ShouldHaveDefaults()
    {
        Application application = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
            "CopyInstanceSettings.applicationMetadata_beforeChange.json"
        );

        Assert.True(application.CopyInstanceSettings.Enabled);
        Assert.True(application.CopyInstanceSettings.IncludeAttachments);
        Assert.False(application.CopyInstanceSettings.IncludeDueBefore);
        Assert.Null(application.CopyInstanceSettings.IncludedDataValues);
        Assert.Null(application.CopyInstanceSettings.IncludedPresentationTexts);
    }

    [Fact]
    public void MetadataWithIncludedValues_ShouldBeDeserialized()
    {
        Application application = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
            "CopyInstanceSettings.applicationMetadata_afterChange.json"
        );

        Assert.True(application.CopyInstanceSettings.IncludeDueBefore);
        Assert.Equal(
            ["appVersion", "customerId"],
            application.CopyInstanceSettings.IncludedDataValues
        );
        Assert.Equal(["name"], application.CopyInstanceSettings.IncludedPresentationTexts);
    }
}
