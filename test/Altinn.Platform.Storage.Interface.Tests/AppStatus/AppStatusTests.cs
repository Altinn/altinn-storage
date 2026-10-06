using System;
using System.Linq;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Newtonsoft.Json;
using Xunit;
using TextJson = System.Text.Json.JsonSerializer;

namespace Altinn.Platform.Storage.Interface.Tests.AppStatus;

/// <summary>
/// Pins the wire contract of <see cref="AppStatus"/> and <see cref="Application.Status"/>. The wire
/// values are the terms of the EU ADMS status vocabulary (http://purl.org/adms/status/), so both
/// serializers must emit the member name unchanged.
/// </summary>
public class AppStatusTests
{
    public static TheoryData<Enums.AppStatus> AllStatuses => [.. Enum.GetValues<Enums.AppStatus>()];

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void WireValue_IsTheMemberName(Enums.AppStatus status)
    {
        string expected = $"\"{status}\"";

        Assert.Equal(expected, JsonConvert.SerializeObject(status));
        Assert.Equal(expected, TextJson.Serialize(status));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void WireValue_RoundTripsThroughBothSerializers(Enums.AppStatus status)
    {
        string json = $"\"{status}\"";

        Assert.Equal(status, JsonConvert.DeserializeObject<Enums.AppStatus>(json));
        Assert.Equal(status, TextJson.Deserialize<Enums.AppStatus>(json));
    }

    [Fact]
    public void WireValues_AreTheAdmsStatusTerms()
    {
        string[] expected = ["Completed", "Deprecated", "UnderDevelopment", "Withdrawn"];

        Assert.Equal(
            expected,
            Enum.GetValues<Enums.AppStatus>().Select(s => JsonConvert.SerializeObject(s).Trim('"'))
        );
        Assert.Equal(
            expected,
            Enum.GetValues<Enums.AppStatus>().Select(s => TextJson.Serialize(s).Trim('"'))
        );
    }

    [Fact]
    public void MetadataWithoutStatus_StatusIsNull()
    {
        const string resourcePath = "AppStatus.applicationMetadata_beforeChange.json";

        Application textJsonApplication =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(resourcePath);
        Application newtonsoftApplication = JsonConvert.DeserializeObject<Application>(
            TestdataHelper.LoadDataFromEmbeddedResourceAsString(resourcePath)
        )!;

        Assert.Null(textJsonApplication.Status);
        Assert.Null(newtonsoftApplication.Status);
    }

    [Fact]
    public void MetadataWithStatus_IsDeserialized()
    {
        const string resourcePath = "AppStatus.applicationMetadata_afterChange.json";

        Application textJsonApplication =
            TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(resourcePath);
        Application newtonsoftApplication = JsonConvert.DeserializeObject<Application>(
            TestdataHelper.LoadDataFromEmbeddedResourceAsString(resourcePath)
        )!;

        Assert.Equal(Enums.AppStatus.UnderDevelopment, textJsonApplication.Status);
        Assert.Equal(Enums.AppStatus.UnderDevelopment, newtonsoftApplication.Status);
    }

    [Fact]
    public void SerializedApplication_CarriesStatusAsString()
    {
        Application application = new() { Id = "ttd/app-status", Status = Enums.AppStatus.Withdrawn };

        string json = JsonConvert.SerializeObject(application);

        Assert.Contains("\"status\":\"Withdrawn\"", json);
        Assert.Equal(Enums.AppStatus.Withdrawn, JsonConvert.DeserializeObject<Application>(json)!.Status);
    }
}
