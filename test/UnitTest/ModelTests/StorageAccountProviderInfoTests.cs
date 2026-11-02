using System;
using System.Text.Json;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class StorageAccountProviderInfoTests
{
    [Fact]
    public void AzureBlob_RoundTripsWithDiscriminatorAndOmitsNullEndpoint()
    {
        StorageAccountProviderInfo info = new AzureBlobProviderInfo(
            "ttdaltinnprodstrg01",
            "ttd-prod-appsdata-blob-db"
        );

        string json = Serialize(info);

        Assert.Equal(
            """{"provider":"azure-blob","accountName":"ttdaltinnprodstrg01","container":"ttd-prod-appsdata-blob-db"}""",
            json
        );
        Assert.Equal(info, Deserialize(json));
    }

    [Fact]
    public void AzureBlob_WithEndpoint_RoundTrips()
    {
        StorageAccountProviderInfo info = new AzureBlobProviderInfo(
            "devstoreaccount1",
            "ttd-dev-appsdata-blob-db",
            new Uri("http://127.0.0.1:10000/devstoreaccount1")
        );

        string json = Serialize(info);

        Assert.Equal(
            """{"provider":"azure-blob","accountName":"devstoreaccount1","container":"ttd-dev-appsdata-blob-db","endpoint":"http://127.0.0.1:10000/devstoreaccount1"}""",
            json
        );
        Assert.Equal(info, Deserialize(json));
    }

    [Fact]
    public void FileSystem_RoundTripsWithDiscriminator()
    {
        StorageAccountProviderInfo info = new FileSystemProviderInfo("/data/blobs/ttd");

        string json = Serialize(info);

        Assert.Equal("""{"provider":"file-system","directory":"/data/blobs/ttd"}""", json);
        Assert.Equal(info, Deserialize(json));
    }

    [Fact]
    public void Deserialize_DiscriminatorAfterOtherProperties_Succeeds()
    {
        // jsonb stores keys ordered by length, so "endpoint" is read back before "provider".
        StorageAccountProviderInfo? info = Deserialize(
            """{"endpoint":"http://127.0.0.1:10000/devstoreaccount1","provider":"azure-blob","container":"c","accountName":"a"}"""
        );

        Assert.Equal(
            new AzureBlobProviderInfo("a", "c", new Uri("http://127.0.0.1:10000/devstoreaccount1")),
            info
        );
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

    [Theory]
    [InlineData("""{"provider":"s3","bucket":"b"}""")]
    [InlineData("""{"accountName":"a","container":"c"}""")]
    [InlineData("""{"provider":"azure-blob","accountName":"a"}""")]
    [InlineData("""{"provider":"azure-blob","accountName":null,"container":"c"}""")]
    [InlineData("""{"provider":"file-system"}""")]
    public void Deserialize_InvalidDocument_Throws(string json)
    {
        Exception exception = Record.Exception(() => Deserialize(json));

        Assert.True(
            exception is JsonException or NotSupportedException,
            $"Unexpected exception: {exception}"
        );
    }

    private static string Serialize(StorageAccountProviderInfo info) =>
        JsonSerializer.Serialize(info, StorageAccountProviderInfo.JsonOptions);

    private static StorageAccountProviderInfo? Deserialize(string json) =>
        JsonSerializer.Deserialize<StorageAccountProviderInfo>(
            json,
            StorageAccountProviderInfo.JsonOptions
        );
}
