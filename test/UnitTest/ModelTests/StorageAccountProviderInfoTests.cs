using System;
using System.Text.Json;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class StorageAccountProviderInfoTests
{
    [Fact]
    public void AzureBlob_SerializesWithoutDiscriminatorAndOmitsNullEndpoint()
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
        Assert.Equal(info, StorageAccountProviderInfo.Deserialize(info.Provider, info.Serialize()));
    }

    [Fact]
    public void AzureBlob_WithEndpoint_SerializesWithoutDiscriminator()
    {
        StorageAccountProviderInfo info = new AzureBlobProviderInfo(
            "devstoreaccount1",
            "ttd-dev-appsdata-blob-db",
            new Uri("http://127.0.0.1:10000/devstoreaccount1")
        );

        Assert.Equal(
            """{"accountName":"devstoreaccount1","container":"ttd-dev-appsdata-blob-db","endpoint":"http://127.0.0.1:10000/devstoreaccount1"}""",
            info.Serialize()
        );
        Assert.Equal(info.Serialize(), info.SerializeToElement().GetRawText());
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
                """{"endpoint":"http://127.0.0.1:10000/devstoreaccount1","container":"c","accountName":"a"}""",
                new AzureBlobProviderInfo(
                    "a",
                    "c",
                    new Uri("http://127.0.0.1:10000/devstoreaccount1")
                )
            },
            {
                StorageAccountProviderInfo.AzureBlob,
                """{"container":"c","accountName":"a"}""",
                new AzureBlobProviderInfo("a", "c")
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

    [Fact]
    public void AzureBlob_ContainerUri_UsesPublicEndpointByDefault()
    {
        AzureBlobProviderInfo info = new("ttdaltinnprodstrg01", "ttd-prod-appsdata-blob-db");

        Assert.Equal(new Uri("https://ttdaltinnprodstrg01.blob.core.windows.net"), info.ServiceUri);
        Assert.Equal(
            new Uri("https://ttdaltinnprodstrg01.blob.core.windows.net/ttd-prod-appsdata-blob-db"),
            info.ContainerUri
        );
    }

    [Theory]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/")]
    public void AzureBlob_ContainerUri_UsesEndpointWhenSet(string endpoint)
    {
        AzureBlobProviderInfo info = new(
            "devstoreaccount1",
            "ttd-dev-appsdata-blob-db",
            new Uri(endpoint)
        );

        Assert.Equal(
            new Uri("http://127.0.0.1:10000/devstoreaccount1/ttd-dev-appsdata-blob-db"),
            info.ContainerUri
        );
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
    [InlineData(StorageAccountProviderInfo.AzureBlob, """{"accountName":"a"}""")]
    [InlineData(StorageAccountProviderInfo.AzureBlob, """{"accountName":null,"container":"c"}""")]
    [InlineData(StorageAccountProviderInfo.AzureBlob, "null")]
    [InlineData(StorageAccountProviderInfo.FileSystem, "{}")]
    [InlineData(StorageAccountProviderInfo.FileSystem, """{"accountName":"a","container":"c"}""")]
    public void Deserialize_InvalidDocumentForProvider_ThrowsJsonException(
        string provider,
        string json
    )
    {
        Assert.Throws<JsonException>(() => StorageAccountProviderInfo.Deserialize(provider, json));
    }
}
