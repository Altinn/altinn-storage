using System;
using System.Text.Json;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class StorageAccountProviderInfoTests
{
    [Fact]
    public void AzureBlob_SerializesAccountNameAndContainerOnly()
    {
        StorageAccountProviderInfo info = new AzureBlobProviderInfo(
            "ttdaltinnprodstrg01",
            "ttd-prod-appsdata-blob-db"
        );

        Assert.Equal(StorageAccountProviderInfo.AzureBlob, info.Provider);
        Assert.Equal(
            """{"accountName":"ttdaltinnprodstrg01","container":"ttd-prod-appsdata-blob-db"}""",
            info.Serialize()
        );
        Assert.Equal(info.Serialize(), info.SerializeToElement().GetRawText());
        Assert.Equal(info, StorageAccountProviderInfo.Deserialize(info.Provider, info.Serialize()));
    }

    [Fact]
    public void FileSystem_SerializesWithoutDiscriminator()
    {
        StorageAccountProviderInfo info = new FileSystemProviderInfo("/data/blobs/ttd");

        Assert.Equal(StorageAccountProviderInfo.FileSystem, info.Provider);
        Assert.Equal("""{"directory":"/data/blobs/ttd"}""", info.Serialize());
    }

    public static TheoryData<string, string, StorageAccountProviderInfo> ReorderedDocuments =>
        new()
        {
            {
                StorageAccountProviderInfo.AzureBlob,
                """{"container":"ttd-container","accountName":"ttdaccount"}""",
                new AzureBlobProviderInfo("ttdaccount", "ttd-container")
            },
            {
                StorageAccountProviderInfo.FileSystem,
                """{"directory":"/data/blobs/ttd"}""",
                new FileSystemProviderInfo("/data/blobs/ttd")
            },
        };

    [Theory]
    [MemberData(nameof(ReorderedDocuments))]
    public void Deserialize_DocumentKeysInAnyOrder_ReturnsVariantOfProvider(
        string provider,
        string json,
        StorageAccountProviderInfo expected
    )
    {
        StorageAccountProviderInfo info = StorageAccountProviderInfo.Deserialize(provider, json);

        Assert.Equal(expected, info);
        Assert.Equal(provider, info.Provider);
        Assert.Equal(info, StorageAccountProviderInfo.Deserialize(provider, info.Serialize()));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abcdefghijklmnopqrstuvwx", "a-b-c")]
    [InlineData("devstoreaccount1", "ttd-dev-appsdata-blob-db")]
    public void AzureBlob_ValidNames_AreAccepted(string accountName, string container)
    {
        AzureBlobProviderInfo info = new(accountName, container);

        Assert.Equal(accountName, info.AccountName);
        Assert.Equal(container, info.Container);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("TtdAccount")]
    [InlineData("ttd-account")]
    [InlineData("evil.com/x")]
    [InlineData("ttdaccount@evil")]
    [InlineData("ttdaccount\n")]
    [InlineData("")]
    [InlineData(null)]
    public void AzureBlob_InvalidAccountName_Throws(string? accountName)
    {
        Assert.Throws<ArgumentException>(() =>
            new AzureBlobProviderInfo(accountName!, "container")
        );
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    [InlineData("Container")]
    [InlineData("-container")]
    [InlineData("container-")]
    [InlineData("con--tainer")]
    [InlineData("con/tainer")]
    [InlineData("container?x=1")]
    [InlineData("container\n")]
    [InlineData("")]
    [InlineData(null)]
    public void AzureBlob_InvalidContainer_Throws(string? container)
    {
        Assert.Throws<ArgumentException>(() => new AzureBlobProviderInfo("ttdaccount", container!));
    }

    [Fact]
    public void Deserialize_UnknownProvider_ThrowsNotSupported()
    {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            StorageAccountProviderInfo.Deserialize("s3", """{"bucket":"b"}""")
        );

        Assert.Contains("'s3'", exception.Message);
    }

    [Theory]
    [InlineData(StorageAccountProviderInfo.AzureBlob, """{"accountName":"ttdaccount"}""")]
    [InlineData(
        StorageAccountProviderInfo.AzureBlob,
        """{"accountName":null,"container":"container"}"""
    )]
    [InlineData(StorageAccountProviderInfo.AzureBlob, "null")]
    [InlineData(
        StorageAccountProviderInfo.AzureBlob,
        """{"accountName":"ttdaccount","container":"container","endpoint":"https://evil.example"}"""
    )]
    [InlineData(
        StorageAccountProviderInfo.AzureBlob,
        """{"accountName":"ttdaccount","container":"container","extra":1}"""
    )]
    [InlineData(
        StorageAccountProviderInfo.AzureBlob,
        """{"accountName":"evil.example/x","container":"container"}"""
    )]
    [InlineData(
        StorageAccountProviderInfo.AzureBlob,
        """{"accountName":"ttdaccount","container":"../other"}"""
    )]
    [InlineData(StorageAccountProviderInfo.FileSystem, "{}")]
    [InlineData(
        StorageAccountProviderInfo.FileSystem,
        """{"directory":"/data/blobs/ttd","extra":1}"""
    )]
    [InlineData(
        StorageAccountProviderInfo.FileSystem,
        """{"accountName":"ttdaccount","container":"container"}"""
    )]
    public void Deserialize_InvalidDocumentForProvider_ThrowsJsonException(
        string provider,
        string json
    )
    {
        Assert.Throws<JsonException>(() => StorageAccountProviderInfo.Deserialize(provider, json));
    }
}
