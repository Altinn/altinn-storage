using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class StorageAccountNamingTests
{
    private static readonly AzureStorageConfiguration _azureConfiguration = new()
    {
        AccountName = "altinnstorage",
        OrgStorageAccount = "{0}altinndevstorage01",
        OrgStorageContainer = "{0}-dev-appsdata-blob-db",
    };

    [Fact]
    public void PatternDefault_DefaultNumber_UsesUnmodifiedPattern()
    {
        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(
            _azureConfiguration,
            "ttd",
            StorageAccountNaming.DefaultStorageAccountNumber
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage01", "ttd-dev-appsdata-blob-db"),
            info
        );
        Assert.Equal(
            new Uri("https://ttdaltinndevstorage01.blob.core.windows.net/ttd-dev-appsdata-blob-db"),
            info.ContainerUri
        );
    }

    [Fact]
    public void PatternDefault_OtherNumber_ReplacesTrailingDigits()
    {
        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(
            _azureConfiguration,
            "ttd",
            7
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage07", "ttd-dev-appsdata-blob-db"),
            info
        );
    }

    [Fact]
    public void PatternDefault_Azurite_UsesConfiguredEndpointForEveryNumber()
    {
        AzureStorageConfiguration configuration = new()
        {
            AccountName = "devstoreaccount1",
            BlobEndPoint = "http://127.0.0.1:10000/devstoreaccount1",
            OrgStorageAccount = "{0}altinndevstorage01",
            OrgStorageContainer = "{0}-dev-appsdata-blob-db",
        };

        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(configuration, "ttd", 7);

        Assert.Equal(
            new AzureBlobProviderInfo(
                "devstoreaccount1",
                "ttd-dev-appsdata-blob-db",
                new Uri("http://127.0.0.1:10000/devstoreaccount1")
            ),
            info
        );
        Assert.Equal(
            new Uri("http://127.0.0.1:10000/devstoreaccount1/ttd-dev-appsdata-blob-db"),
            info.ContainerUri
        );
    }

    [Fact]
    public void NumberOf_ApplicationWithoutNumber_ReturnsDefault()
    {
        Assert.Equal(1, StorageAccountNaming.NumberOf(new Application()));
    }

    [Fact]
    public void NumberOf_ApplicationWithNumber_ReturnsNumber()
    {
        Assert.Equal(
            3,
            StorageAccountNaming.NumberOf(new Application { StorageAccountNumber = 3 })
        );
    }
}
