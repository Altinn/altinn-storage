#nullable disable

using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Configuration;

public class GeneralSettingsTests
{
    private const string InstanceGuid = "1916cd18-3b8e-46f8-aeaf-4bc3397ddd08";

    [Fact]
    public void SetPlatformSelfLinks_NoPlatformBaseUrl_LinksStartWithPlatformHost()
    {
        // Arrange
        GeneralSettings settings = new() { Hostname = "at22.altinn.cloud" };
        Instance instance = CreateInstance();

        // Act
        settings.SetPlatformSelfLinks(instance);

        // Assert
        Assert.Equal(
            $"https://platform.at22.altinn.cloud/storage/api/v1/instances/1337/{InstanceGuid}",
            instance.SelfLinks.Platform
        );
        Assert.Equal(
            $"https://platform.at22.altinn.cloud/storage/api/v1/instances/1337/{InstanceGuid}/data/data-1",
            instance.Data[0].SelfLinks.Platform
        );
    }

    [Theory]
    [InlineData("http://local.altinn.cloud:8000")]
    [InlineData("http://local.altinn.cloud:8000/")]
    public void SetPlatformSelfLinks_PlatformBaseUrl_LinksStartWithIt(string platformBaseUrl)
    {
        // Arrange
        GeneralSettings settings = new()
        {
            Hostname = "at22.altinn.cloud",
            PlatformBaseUrl = platformBaseUrl,
        };
        Instance instance = CreateInstance();

        // Act
        settings.SetPlatformSelfLinks(instance);

        // Assert
        Assert.Equal(
            $"http://local.altinn.cloud:8000/storage/api/v1/instances/1337/{InstanceGuid}",
            instance.SelfLinks.Platform
        );
        Assert.Equal(
            $"http://local.altinn.cloud:8000/storage/api/v1/instances/1337/{InstanceGuid}/data/data-1",
            instance.Data[0].SelfLinks.Platform
        );
    }

    [Fact]
    public void SetPlatformSelfLinks_DataElement_KeepsTheAppsLink()
    {
        // Arrange
        GeneralSettings settings = new() { PlatformBaseUrl = "http://local.altinn.cloud:8000" };
        DataElement dataElement = new()
        {
            Id = "data-1",
            InstanceGuid = InstanceGuid,
            SelfLinks = new ResourceLinks { Apps = "https://ttd.apps.at22.altinn.cloud/data-1" },
        };

        // Act
        settings.SetPlatformSelfLinks(dataElement, 1337);

        // Assert
        Assert.Equal(
            $"http://local.altinn.cloud:8000/storage/api/v1/instances/1337/{InstanceGuid}/data/data-1",
            dataElement.SelfLinks.Platform
        );
        Assert.Equal("https://ttd.apps.at22.altinn.cloud/data-1", dataElement.SelfLinks.Apps);
    }

#pragma warning disable CS0618 // The obsolete methods are the reference for the default links.
    [Fact]
    public void SetPlatformSelfLinks_NoPlatformBaseUrl_SameAsTheObsoleteMethods()
    {
        // Arrange
        GeneralSettings settings = new() { Hostname = "at22.altinn.cloud" };
        Instance expected = CreateInstance();
        expected.SetPlatformSelfLinks("at22.altinn.cloud/storage/api/v1/");
        Instance actual = CreateInstance();

        // Act
        settings.SetPlatformSelfLinks(actual);

        // Assert
        Assert.Equal(expected.SelfLinks.Platform, actual.SelfLinks.Platform);
        Assert.Equal(expected.Data[0].SelfLinks.Platform, actual.Data[0].SelfLinks.Platform);
    }
#pragma warning restore CS0618

    private static Instance CreateInstance()
    {
        return new()
        {
            Id = $"1337/{InstanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = "1337" },
            Data = new List<DataElement>
            {
                new() { Id = "data-1", InstanceGuid = InstanceGuid },
            },
        };
    }
}
