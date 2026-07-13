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
}
