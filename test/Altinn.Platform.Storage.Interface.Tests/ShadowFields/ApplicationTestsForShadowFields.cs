using Altinn.Platform.Storage.Interface.Models;
using FluentAssertions;
using System.Linq;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests
{
    public class ApplicationTestsForShadowFields
    {
        [Fact]
        public void MetadataWithoutShadowFields_ShouldBeFalse()
        {
            Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("ShadowFields.applicationMetadata_beforeChange.json");

            applicationBefore.DataTypes.Where(d => d.Id == "Veileder").First().AppLogic.ShadowFields.Should().BeNull();
        }

        [Fact]
        public void MetadataWithShadowFields_ShouldBeTrue()
        {
            Application applicationBefore = TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>("ShadowFields.applicationMetadata_afterChange.json");

            applicationBefore.DataTypes.Where(d => d.Id == "Veileder").First().AppLogic.ShadowFields.Prefix.Should().Be("AltinnSF_");
            applicationBefore.DataTypes.Where(d => d.Id == "Veileder").First().AppLogic.ShadowFields.SaveToDataType.Should().Be("model-clean");

        }
    }
}
