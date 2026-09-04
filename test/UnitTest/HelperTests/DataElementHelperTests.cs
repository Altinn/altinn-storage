#nullable disable

using System;
using System.Buffers.Text;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class DataElementHelperTests
{
    [Fact]
    public void GetVersionedBlobPath_WithVersionId_UsesDataElementsPath()
    {
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        Guid instanceGuid = Guid.NewGuid();

        string result = DataElementHelper.GetVersionedBlobPath(
            "ttd/app",
            instanceGuid,
            blobVersionId
        );

        Assert.Equal($"ttd/app/{instanceGuid}/data-elements/{blobVersionId}", result);
    }

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
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();

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
            .Replace("{instanceGuid}", instanceGuid.ToString())
            .Replace("{dataElementId}", dataElementId.ToString())
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
}
