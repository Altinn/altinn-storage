using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class BlobRepositoryTests
{
    [Fact]
    public void ValidateBlobVersionId_WithVersionId_DoesNotThrow()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: true);

        repo.ValidateBlobVersionId("2024-01-15T10:30:00.0000000Z", "some/blob/path");
    }

    [Fact]
    public void ValidateBlobVersionId_NullVersionId_StrictEnabled_Throws()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: true);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            repo.ValidateBlobVersionId(null, "some/blob/path")
        );
        Assert.Contains("did not return a version ID", ex.Message);
        Assert.Contains("some/blob/path", ex.Message);
        Assert.Contains("blob versioning", ex.Message);
    }

    [Fact]
    public void ValidateBlobVersionId_EmptyVersionId_StrictEnabled_Throws()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: true);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            repo.ValidateBlobVersionId(string.Empty, "org/app/instance/data")
        );
        Assert.Contains("did not return a version ID", ex.Message);
        Assert.Contains("org/app/instance/data", ex.Message);
    }

    [Fact]
    public void ValidateBlobVersionId_NullVersionId_StrictDisabled_DoesNotThrow()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: false);

        repo.ValidateBlobVersionId(null, "some/blob/path");
    }

    [Fact]
    public void ValidateBlobVersionId_EmptyVersionId_StrictDisabled_DoesNotThrow()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: false);

        repo.ValidateBlobVersionId(string.Empty, "some/blob/path");
    }

    [Fact]
    public void ValidateBlobVersionId_WithVersionId_StrictDisabled_DoesNotThrow()
    {
        // Arrange
        var (repo, _) = CreateRepository(requireBlobVersionId: false);

        // Act
        repo.ValidateBlobVersionId("2024-01-15T10:30:00.0000000Z", "some/blob/path");
    }

    private static (BlobRepository Repo, Mock<ILogger<BlobRepository>> LoggerMock) CreateRepository(
        bool requireBlobVersionId
    )
    {
        var config = new AzureStorageConfiguration
        {
            AccountName = "devstoreaccount1",
            AccountKey =
                "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
            BlobEndPoint = "http://127.0.0.1:10000/devstoreaccount1",
            OrgStorageAccount = "{0}altinndevstrg01",
            OrgStorageContainer = "{0}-dev-appsdata-blob-db",
            RequireBlobVersionIdOnWrite = requireBlobVersionId,
        };

        var loggerMock = new Mock<ILogger<BlobRepository>>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(config);

        var repo = new BlobRepository(memoryCache, options, loggerMock.Object);

        return (repo, loggerMock);
    }
}
