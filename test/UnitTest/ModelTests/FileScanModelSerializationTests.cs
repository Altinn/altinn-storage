using System;
using System.Text.Json;
using Altinn.Platform.Storage.Models;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

/// <summary>
/// The file scanner reads the status of these responses as strings, so every enum value must serialize as its
/// camel-cased name, never as a number, including values added later.
/// </summary>
public class FileScanModelSerializationTests
{
    public static TheoryData<FileScanTargetStatus> TargetStatuses =>
        new(Enum.GetValues<FileScanTargetStatus>());

    public static TheoryData<FileScanStatusOutcome> StatusOutcomes =>
        new(Enum.GetValues<FileScanStatusOutcome>());

    [Theory]
    [MemberData(nameof(TargetStatuses))]
    public void FileScanTarget_Status_SerializesAsCamelCaseString(FileScanTargetStatus status)
    {
        string json = JsonConvert.SerializeObject(new FileScanTarget(status));

        Assert.Equal($$"""{"Status":"{{CamelCase(status)}}"}""", json);
    }

    [Theory]
    [MemberData(nameof(StatusOutcomes))]
    public void FileScanStatusResponse_Status_SerializesAsCamelCaseString(
        FileScanStatusOutcome outcome
    )
    {
        string json = JsonConvert.SerializeObject(new FileScanStatusResponse(outcome));

        Assert.Equal($$"""{"Status":"{{CamelCase(outcome)}}"}""", json);
    }

    private static string CamelCase(Enum value) =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
