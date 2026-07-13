#nullable disable

using System;
using System.Buffers.Text;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class DataElementHelperTests
{
    [Theory]
    [InlineData("{appId}/{instanceGuid}/data/{dataElementId}", true)]
    [InlineData("{appId}/{instanceGuid}/data-elements/{blobVersionId}", true)]
    [InlineData(null, false)]
    [InlineData("{appId}/{instanceGuid}/data/{otherDataElementId}", false)]
    [InlineData("{appId}/{otherInstanceGuid}/data-elements/{blobVersionId}", false)]
    [InlineData("{otherAppId}/{instanceGuid}/data-elements/{blobVersionId}", false)]
    [InlineData("{appId}/{instanceGuid}/data-elements/", false)]
    [InlineData("{appId}/{instanceGuid}/data-elements/nested/{blobVersionId}", false)]
    [InlineData("{appId}/{instanceGuid}/data-elements/.", false)]
    [InlineData("{appId}/{instanceGuid}/data-elements/..", false)]
    [InlineData("ondemand/{appId}/{instanceGuid}/data/{dataElementId}", false)]
    public void IsExpectedBlobStoragePath_ReturnsWhetherPathBelongsToDataElement(
        string blobStoragePathTemplate,
        bool expected
    )
    {
        // Arrange
        string appId = "ttd/app";
        string instanceGuid = $"{Guid.NewGuid()}";
        string dataElementId = $"{Guid.NewGuid()}";

        // The blob versioning build encodes the version id as base64url of a v7 guid in
        // canonical byte order, which keeps the encoded id sorted by creation time.
        string blobVersionId = Base64Url.EncodeToString(
            Guid.CreateVersion7().ToByteArray(bigEndian: true)
        );

        string blobStoragePath = blobStoragePathTemplate
            ?.Replace("{otherAppId}", "ttd/other-app")
            .Replace("{otherInstanceGuid}", $"{Guid.NewGuid()}")
            .Replace("{otherDataElementId}", $"{Guid.NewGuid()}")
            .Replace("{appId}", appId)
            .Replace("{instanceGuid}", instanceGuid)
            .Replace("{dataElementId}", dataElementId)
            .Replace("{blobVersionId}", blobVersionId);

        // Act
        bool actual = DataElementHelper.IsExpectedBlobStoragePath(
            blobStoragePath,
            appId,
            instanceGuid,
            dataElementId
        );

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateDataElement_GeneratedFromTaskProvided_DataElementReferencesPopulated()
    {
        // AAct
        var actual = DataElementHelper.CreateDataElement(
            "dataType",
            null,
            new InstanceInternal { AppId = "ttd/app-test", Id = Guid.NewGuid().ToString() },
            DateTime.UtcNow,
            "application/json",
            "file-name.json",
            1234,
            "12345",
            "Task_1"
        );

        // Assert
        Assert.NotEmpty(actual.References);
        Assert.Equal(RelationType.GeneratedFrom, actual.References[0].Relation);
        Assert.Equal(ReferenceType.Task, actual.References[0].ValueType);
    }

    [Fact]
    public void CreateDataElement_NoGeneratedFromIdsProvided_DataElementReferencesIsNull()
    {
        // Act
        var actual = DataElementHelper.CreateDataElement(
            "dataType",
            null,
            new InstanceInternal { AppId = "ttd/app-test", Id = Guid.NewGuid().ToString() },
            DateTime.UtcNow,
            "application/json",
            "file-name.json",
            1234,
            "12345",
            null
        );

        // Assert
        Assert.Null(actual.References);
    }
}
