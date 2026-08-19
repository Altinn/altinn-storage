#nullable disable

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class JsonHelperTests
{
    private static readonly DateTime _hardDeleted = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A nested object whose type is absent from the white list is serialized in full. The data
    /// element update path relies on this to write a complete deleteStatus.
    /// </summary>
    [Fact]
    public void Serialize_PerType_NestedTypeNotListed_SerializedInFull()
    {
        // Arrange
        DataElement element = new()
        {
            Locked = true,
            DeleteStatus = new DeleteStatus { IsHardDeleted = true, HardDeleted = _hardDeleted },
        };
        Dictionary<Type, List<string>> propertiesToSerialize = new()
        {
            [typeof(DataElement)] = [nameof(DataElement.DeleteStatus)],
        };

        // Act
        string json = CustomSerializer.Serialize(element, propertiesToSerialize);

        // Assert
        JsonNode node = JsonNode.Parse(json);
        Assert.Null(node[nameof(DataElement.Locked)]);
        JsonNode deleteStatus = node[nameof(DataElement.DeleteStatus)];
        Assert.True(deleteStatus[nameof(DeleteStatus.IsHardDeleted)].GetValue<bool>());
        Assert.Equal(
            _hardDeleted,
            deleteStatus[nameof(DeleteStatus.HardDeleted)].GetValue<DateTime>()
        );
    }

    /// <summary>
    /// A nested object whose type is listed is trimmed to the properties listed for that type.
    /// </summary>
    [Fact]
    public void Serialize_PerType_NestedTypeListed_TrimmedToListedProperties()
    {
        // Arrange
        DataElement element = new()
        {
            DeleteStatus = new DeleteStatus { IsHardDeleted = true, HardDeleted = _hardDeleted },
        };
        Dictionary<Type, List<string>> propertiesToSerialize = new()
        {
            [typeof(DataElement)] = [nameof(DataElement.DeleteStatus)],
            [typeof(DeleteStatus)] = [nameof(DeleteStatus.IsHardDeleted)],
        };

        // Act
        string json = CustomSerializer.Serialize(element, propertiesToSerialize);

        // Assert
        JsonNode deleteStatus = JsonNode.Parse(json)[nameof(DataElement.DeleteStatus)];
        Assert.True(deleteStatus[nameof(DeleteStatus.IsHardDeleted)].GetValue<bool>());
        Assert.Null(deleteStatus[nameof(DeleteStatus.HardDeleted)]);
    }

    /// <summary>
    /// Property names are matched per type, so listing a property on one type does not expose a
    /// same-named property on an unrelated type.
    /// </summary>
    [Fact]
    public void Serialize_PerType_SameNamedPropertyOnOtherType_NotAffected()
    {
        // Arrange
        DataElement element = new()
        {
            Metadata = [new KeyValueEntry { Key = "key1", Value = "value1" }],
            References =
            [
                new Reference
                {
                    Value = "9c1e0ea0-1a6a-4a35-a1b3-4b6a9a1b0a1a",
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.DataElement,
                },
            ],
        };
        Dictionary<Type, List<string>> propertiesToSerialize = new()
        {
            [typeof(DataElement)] = [nameof(DataElement.Metadata), nameof(DataElement.References)],
            [typeof(KeyValueEntry)] = [nameof(KeyValueEntry.Value)],
        };

        // Act
        string json = CustomSerializer.Serialize(element, propertiesToSerialize);

        // Assert
        JsonNode node = JsonNode.Parse(json);
        JsonNode metadata = node[nameof(DataElement.Metadata)][0];
        Assert.Null(metadata[nameof(KeyValueEntry.Key)]);
        Assert.Equal("value1", metadata[nameof(KeyValueEntry.Value)].GetValue<string>());

        JsonNode reference = node[nameof(DataElement.References)][0];
        Assert.Equal(
            "9c1e0ea0-1a6a-4a35-a1b3-4b6a9a1b0a1a",
            reference[nameof(Reference.Value)].GetValue<string>()
        );
        Assert.Equal(
            nameof(RelationType.GeneratedFrom),
            reference[nameof(Reference.Relation)].GetValue<string>()
        );
        Assert.Equal(
            nameof(ReferenceType.DataElement),
            reference[nameof(Reference.ValueType)].GetValue<string>()
        );
    }

    /// <summary>
    /// The flat white list is applied at every level of the graph. The instance update path depends
    /// on this to trim a nested status to the properties its callers list.
    /// </summary>
    [Fact]
    public void Serialize_FlatList_AppliedToNestedTypesAsWell()
    {
        // Arrange
        Instance instance = new()
        {
            Org = "ttd",
            Status = new InstanceStatus { IsSoftDeleted = true, IsArchived = true },
        };
        List<string> propertiesToSerialize =
        [
            nameof(Instance.Status),
            nameof(InstanceStatus.IsSoftDeleted),
        ];

        // Act
        string json = CustomSerializer.Serialize(instance, propertiesToSerialize);

        // Assert
        JsonNode node = JsonNode.Parse(json);
        Assert.Null(node[nameof(Instance.Org)]);
        JsonNode status = node[nameof(Instance.Status)];
        Assert.True(status[nameof(InstanceStatus.IsSoftDeleted)].GetValue<bool>());
        Assert.Null(status[nameof(InstanceStatus.IsArchived)]);
    }
}
