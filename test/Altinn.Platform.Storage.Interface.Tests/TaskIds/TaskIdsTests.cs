using System;
using System.Linq;
using System.Text.Json;
using Altinn.Platform.Storage.Interface.Models;
using Newtonsoft.Json;
using Xunit;
using TextJson = System.Text.Json.JsonSerializer;

// v8 apps still upload the obsolete TaskId, so its wire format stays pinned.
#pragma warning disable CS0618 // Type or member is obsolete

namespace Altinn.Platform.Storage.Interface.Tests.TaskIds;

/// <summary>
/// Pins the wire contract of <see cref="DataType.TaskIds"/> and <see cref="DataType.TaskId"/>.
/// </summary>
public class TaskIdsTests
{
    private const string BeforeChangePath = "TaskIds.applicationMetadata_beforeChange.json";
    private const string AfterChangePath = "TaskIds.applicationMetadata_afterChange.json";

    public enum Serializer
    {
        Newtonsoft,
        TextJson,
    }

    public static TheoryData<Serializer, string[]?, string?> SerializationCases =>
        new()
        {
            { Serializer.Newtonsoft, null, null },
            { Serializer.Newtonsoft, [], "[]" },
            { Serializer.Newtonsoft, ["Task_1", "Task_2"], "[\"Task_1\",\"Task_2\"]" },
            { Serializer.TextJson, null, null },
            { Serializer.TextJson, [], "[]" },
            { Serializer.TextJson, ["Task_1", "Task_2"], "[\"Task_1\",\"Task_2\"]" },
        };

    [Theory]
    [MemberData(nameof(SerializationCases))]
    public void TaskIds_RoundTripsAndIsOmittedWhenNull(
        Serializer serializer,
        string[]? taskIds,
        string? expectedJson
    )
    {
        DataType dataType = new() { Id = "model", TaskIds = taskIds?.ToList() };

        string json = Serialize(dataType, serializer);

        Assert.Equal(expectedJson, GetRawProperty(json, "taskIds"));
        Assert.Equal(taskIds, Deserialize<DataType>(json, serializer).TaskIds);
    }

    [Theory]
    [InlineData(Serializer.Newtonsoft)]
    [InlineData(Serializer.TextJson)]
    public void TaskId_RoundTripsWithoutSettingTaskIds(Serializer serializer)
    {
        DataType dataType = new() { Id = "model", TaskId = "Task_1" };

        string json = Serialize(dataType, serializer);

        Assert.Equal("\"Task_1\"", GetRawProperty(json, "taskId"));
        Assert.Null(GetRawProperty(json, "taskIds"));
        DataType deserialized = Deserialize<DataType>(json, serializer);
        Assert.Equal("Task_1", deserialized.TaskId);
        Assert.Null(deserialized.TaskIds);
    }

    [Theory]
    [InlineData(Serializer.Newtonsoft)]
    [InlineData(Serializer.TextJson)]
    public void MetadataWithoutTaskIds_TaskIdsIsNull(Serializer serializer)
    {
        Application application = LoadApplication(BeforeChangePath, serializer);

        Assert.All(application.DataTypes, dataType => Assert.Null(dataType.TaskIds));
        Assert.Equal("Task_1", DataTypeById(application, "model").TaskId);
        Assert.Null(DataTypeById(application, "attachment").TaskId);
    }

    [Theory]
    [InlineData(Serializer.Newtonsoft)]
    [InlineData(Serializer.TextJson)]
    public void MetadataWithTaskIds_IsDeserialized(Serializer serializer)
    {
        Application application = LoadApplication(AfterChangePath, serializer);

        DataType taskIdsOnly = DataTypeById(application, "taskids-only");
        Assert.Equal(["Task_1", "Task_2"], taskIdsOnly.TaskIds);
        Assert.Null(taskIdsOnly.TaskId);

        DataType both = DataTypeById(application, "taskids-and-taskid");
        Assert.Equal(["Task_2", "Task_3"], both.TaskIds);
        Assert.Equal("Task_1", both.TaskId);

        DataType emptyTaskIds = DataTypeById(application, "empty-taskids-and-taskid");
        Assert.NotNull(emptyTaskIds.TaskIds);
        Assert.Empty(emptyTaskIds.TaskIds);
        Assert.Equal("Task_1", emptyTaskIds.TaskId);
    }

    private static string? GetRawProperty(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out JsonElement value)
            ? value.GetRawText()
            : null;
    }

    private static string Serialize(DataType dataType, Serializer serializer) =>
        serializer switch
        {
            Serializer.Newtonsoft => JsonConvert.SerializeObject(dataType),
            Serializer.TextJson => TextJson.Serialize(dataType, JsonSerializerOptions.Web),
            _ => throw new ArgumentOutOfRangeException(nameof(serializer)),
        };

    private static T Deserialize<T>(string json, Serializer serializer) =>
        serializer switch
        {
            Serializer.Newtonsoft => JsonConvert.DeserializeObject<T>(json)!,
            Serializer.TextJson => TextJson.Deserialize<T>(json, JsonSerializerOptions.Web)!,
            _ => throw new ArgumentOutOfRangeException(nameof(serializer)),
        };

    private static Application LoadApplication(string resourcePath, Serializer serializer) =>
        serializer switch
        {
            Serializer.Newtonsoft => JsonConvert.DeserializeObject<Application>(
                TestdataHelper.LoadDataFromEmbeddedResourceAsString(resourcePath)
            )!,
            Serializer.TextJson => TestdataHelper.LoadDataFromEmbeddedResourceAsType<Application>(
                resourcePath
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(serializer)),
        };

    private static DataType DataTypeById(Application application, string id) =>
        application.DataTypes.Single(d => d.Id == id);
}
