using System;
using System.Linq;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Newtonsoft.Json;
using Xunit;
using TextJson = System.Text.Json.JsonSerializer;

namespace Altinn.Platform.Storage.Interface.Tests;

/// <summary>
/// Pins the wire contract of <see cref="ProcessStatus"/>. Storage compares the persisted spelling in
/// SQL and its conflict messages lowercase the member name to reproduce it, so both serializers must
/// agree on that spelling, and neither may accept or emit the numeric form.
/// </summary>
public class ProcessStatusTests
{
    public static TheoryData<ProcessStatus> AllStatuses => [.. Enum.GetValues<ProcessStatus>()];

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void WireValue_IsTheLowercaseMemberName(ProcessStatus status)
    {
        string expected = $"\"{status.ToString().ToLowerInvariant()}\"";

        Assert.Equal(expected, JsonConvert.SerializeObject(status));
        Assert.Equal(expected, TextJson.Serialize(status));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void WireValue_RoundTripsThroughBothSerializers(ProcessStatus status)
    {
        string json = $"\"{status.ToString().ToLowerInvariant()}\"";

        Assert.Equal(status, JsonConvert.DeserializeObject<ProcessStatus>(json));
        Assert.Equal(status, TextJson.Deserialize<ProcessStatus>(json));
    }

    [Fact]
    public void WireValues_AreIdleAndProcessing()
    {
        Assert.Equal("\"idle\"", JsonConvert.SerializeObject(ProcessStatus.Idle));
        Assert.Equal("\"processing\"", TextJson.Serialize(ProcessStatus.Processing));
    }

    [Fact]
    public void NumericForm_IsRejectedByBothSerializers()
    {
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<ProcessState>("""{"status":0}""")
        );
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<ProcessState>("""{"status":"0"}""")
        );
        Assert.Throws<System.Text.Json.JsonException>(() =>
            TextJson.Deserialize<ProcessState>("""{"Status":0}""")
        );
    }

    [Fact]
    public void UndeclaredStatus_IsRejectedRatherThanCarried()
    {
        ProcessState undeclared = new() { Status = (ProcessStatus)99 };

        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<ProcessState>("""{"status":"archived"}""")
        );
        Assert.Throws<JsonSerializationException>(() => JsonConvert.SerializeObject(undeclared));
        Assert.Throws<System.Text.Json.JsonException>(() => TextJson.Serialize(undeclared));
    }

    [Fact]
    public void AbsentStatus_StaysAbsentInBothDirections()
    {
        Assert.Null(JsonConvert.DeserializeObject<ProcessState>("{}")!.Status);
        Assert.Null(TextJson.Deserialize<ProcessState>("{}")!.Status);
        Assert.DoesNotContain(
            "status",
            JsonConvert.SerializeObject(new ProcessState()),
            StringComparison.OrdinalIgnoreCase
        );
    }
}
