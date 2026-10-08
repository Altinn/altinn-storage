using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class DataElementContentServiceTests
{
    private const string Org = "ttd";
    private const string AppId = "ttd/apps-test";
    private const string DataTypeId = "default";

    [Fact]
    public async Task OpenContent_StoredBlob_ReadsFromBlobStorage()
    {
        Fixture fixture = new();
        string blobStoragePath = $"{AppId}/{fixture.Instance.Id}/data/{fixture.DataElement.Id}";
        fixture.DataElement.BlobStoragePath = blobStoragePath;

        Stream content = await fixture.OpenContent("nb");

        Assert.NotNull(content);
        fixture.BlobRepository.Verify(
            repository =>
                repository.ReadBlob(
                    Org,
                    blobStoragePath,
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.OnDemandContentService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenContent_BlobStoragePathOutsideInstance_Throws()
    {
        Fixture fixture = new();
        fixture.DataElement.BlobStoragePath = $"{AppId}/{Guid.NewGuid()}/data/{Guid.NewGuid()}";

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.OpenContent("nb"));

        fixture.BlobRepository.Verify(
            repository =>
                repository.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task OpenContent_OnDemandPath_GeneratesContent()
    {
        Fixture fixture = new();
        fixture.DataElement.BlobStoragePath = "ondemand/formdatapdf";

        await fixture.OpenContent("nn");

        fixture.OnDemandContentService.Verify(
            service =>
                service.GetContent(
                    "formdatapdf",
                    "apps-test",
                    fixture.Instance.Id,
                    fixture.DataElement.Id,
                    "nn",
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        fixture.BlobRepository.Verify(
            repository =>
                repository.ReadBlob(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Theory]
    [InlineData("ttd/a2-1234", true, "ttd")]
    [InlineData("ttd/a2-1234", false, "digdir")]
    [InlineData("digdir/apps-test", true, "digdir")]
    public async Task OpenContent_ReadsBlobAsTheConfiguredServiceOwner(
        string appId,
        bool useTtdAsServiceOwner,
        string expectedOrg
    )
    {
        Fixture fixture = new() { A2UseTtdAsServiceOwner = useTtdAsServiceOwner };
        fixture.Instance.Org = "digdir";
        fixture.Instance.AppId = appId;
        fixture.DataElement.BlobStoragePath =
            $"{appId}/{fixture.Instance.Id}/data/{fixture.DataElement.Id}";

        await fixture.OpenContent("nb");

        fixture.BlobRepository.Verify(
            repository =>
                repository.ReadBlob(
                    expectedOrg,
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private sealed class Fixture
    {
        public Mock<IBlobRepository> BlobRepository { get; } = new();

        public Mock<IOnDemandContentService> OnDemandContentService { get; } = new();

        public InstanceInternal Instance { get; } =
            new()
            {
                Id = Guid.NewGuid(),
                AppId = AppId,
                Org = Org,
            };

        public DataElementInternal DataElement { get; } =
            new() { Id = Guid.NewGuid(), DataType = DataTypeId };

        public Application Application { get; } =
            new() { DataTypes = [new DataType { Id = DataTypeId }] };

        public bool A2UseTtdAsServiceOwner { get; init; }

        public Task<Stream> OpenContent(string language)
        {
            BlobRepository
                .Setup(repository =>
                    repository.ReadBlob(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<int?>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("blob content")));

            DataElementContentService target = new(
                BlobRepository.Object,
                OnDemandContentService.Object,
                Options.Create(
                    new GeneralSettings { A2UseTtdAsServiceOwner = A2UseTtdAsServiceOwner }
                )
            );

            return target.OpenContent(
                Instance,
                DataElement,
                Application,
                language,
                CancellationToken.None
            );
        }
    }
}
