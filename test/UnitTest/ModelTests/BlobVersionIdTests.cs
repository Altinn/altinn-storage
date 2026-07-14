using System;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class BlobVersionIdTests
{
    [Fact]
    public void Encode_UsesBase64UrlEncodedUuidBytes()
    {
        Guid version = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string encoded = BlobVersionId.Encode(version);

        Assert.Equal("EREREREREREREREREREREQ", encoded);
        Assert.Equal(22, encoded.Length);
        Assert.Equal(version, BlobVersionId.Decode(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToContentEtag_WithoutBlobVersionId_ReturnsNull(string? blobVersionId)
    {
        Assert.Null(BlobVersionId.ToContentEtag(blobVersionId));
    }

    [Fact]
    public void ToContentEtag_WithBlobVersionId_ReturnsQuotedValue()
    {
        const string blobVersionId = "EREREREREREREREREREREQ";

        Assert.Equal($"\"{blobVersionId}\"", BlobVersionId.ToContentEtag(blobVersionId));
    }

    [Fact]
    public void TryParseContentEtag_WithValidStrongEtag_ReturnsBlobVersionId()
    {
        const string blobVersionId = "EREREREREREREREREREREQ";

        bool parsed = BlobVersionId.TryParseContentEtag($"\"{blobVersionId}\"", out string? actual);

        Assert.True(parsed);
        Assert.Equal(blobVersionId, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EREREREREREREREREREREQ")]
    [InlineData("W/\"EREREREREREREREREREREQ\"")]
    [InlineData("*")]
    [InlineData("\"\"")]
    [InlineData("\"")]
    [InlineData("EREREREREREREREREREREQ\"")]
    [InlineData("\"EREREREREREREREREREREQ")]
    [InlineData("\"ERERERERER\"EREREREREREQ\"")]
    [InlineData("\"ERERERERER\\\"EREREREREREQ\"")]
    [InlineData(" \"EREREREREREREREREREREQ\" ")]
    [InlineData("\"ERERERERER\u0001EREREREREREQ\"")]
    [InlineData("\"ERERERERERERERERERERE!\"")]
    [InlineData("\"ERERERERERERERERERERE\"")]
    public void TryParseContentEtag_WithInvalidValue_ReturnsFalse(string? etag)
    {
        bool parsed = BlobVersionId.TryParseContentEtag(etag, out string? blobVersionId);

        Assert.False(parsed);
        Assert.Null(blobVersionId);
    }
}
