using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
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
    private const int InstanceOwnerPartyId = 12345;

    [Fact]
    public async Task ResolveForRead_InstanceNotFound_ReturnsNotFound()
    {
        Guid instanceGuid = Guid.NewGuid();
        Fixture fixture = new() { Instance = null };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                instanceGuid,
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(404, serviceError.ErrorCode);
        Assert.Equal(
            $"Unable to find any instance with id: {InstanceOwnerPartyId}/{instanceGuid}.",
            serviceError.ErrorMessage
        );
    }

    [Fact]
    public async Task ResolveForRead_InstanceReadNotAuthorized_ReturnsForbidden()
    {
        Fixture fixture = new() { Authorized = false };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(403, serviceError.ErrorCode);
    }

    [Fact]
    public async Task ResolveForRead_DataElementNotFound_ReturnsNotFound()
    {
        Guid dataGuid = Guid.NewGuid();
        Fixture fixture = new() { DataElement = null };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(InstanceOwnerPartyId, Guid.NewGuid(), dataGuid, CancellationToken.None);

        Assert.Null(context);
        Assert.Equal(404, serviceError.ErrorCode);
        Assert.Equal(
            $"Unable to find any data element with id: {dataGuid}.",
            serviceError.ErrorMessage
        );
    }

    [Fact]
    public async Task ResolveForRead_ApplicationNotFound_ReturnsNotFound()
    {
        Fixture fixture = new() { Application = null };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(404, serviceError.ErrorCode);
        Assert.Equal($"Cannot find application {AppId} in storage", serviceError.ErrorMessage);
    }

    [Fact]
    public async Task ResolveForRead_DataTypeNotDeclaredInApplication_ReturnsBadRequest()
    {
        Fixture fixture = new()
        {
            Application = new Application { DataTypes = [new DataType { Id = "some-other-type" }] },
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(400, serviceError.ErrorCode);
        Assert.Equal(
            "Requested element type is not declared in application metadata",
            serviceError.ErrorMessage
        );
    }

    [Fact]
    public async Task ResolveForRead_ActionRequiredToReadNotGranted_ReturnsForbidden()
    {
        Fixture fixture = new()
        {
            Application = new Application
            {
                DataTypes = [new DataType { Id = DataTypeId, ActionRequiredToRead = "sign" }],
            },
            ActionAuthorized = false,
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(403, serviceError.ErrorCode);
    }

    [Fact]
    public async Task ResolveForRead_HardDeletedElement_IsResolved()
    {
        Fixture fixture = new();
        fixture.DataElement!.DeleteStatus = new DeleteStatus { IsHardDeleted = true };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.Null(serviceError);
        Assert.True(context.DataElement.DeleteStatus.IsHardDeleted);
    }

    [Fact]
    public async Task OpenContent_StoredBlob_ReadsFromBlobStorage()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataGuid = Guid.NewGuid();
        Fixture fixture = new();
        fixture.DataElement!.BlobStoragePath = $"{AppId}/{instanceGuid}/data/{dataGuid}";

        DataElementContentService target = fixture.Build();
        (DataElementReadContext context, _) = await target.ResolveForRead(
            InstanceOwnerPartyId,
            instanceGuid,
            dataGuid,
            CancellationToken.None
        );

        Stream content = await target.OpenContent(context, "nb", CancellationToken.None);

        Assert.NotNull(content);
        fixture.BlobRepository.Verify(
            repository =>
                repository.ReadBlob(
                    Org,
                    $"{AppId}/{instanceGuid}/data/{dataGuid}",
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
        fixture.DataElement!.BlobStoragePath = $"{AppId}/{Guid.NewGuid()}/data/{Guid.NewGuid()}";

        DataElementContentService target = fixture.Build();
        (DataElementReadContext context, _) = await target.ResolveForRead(
            InstanceOwnerPartyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            target.OpenContent(context, "nb", CancellationToken.None)
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

    [Fact]
    public async Task OpenContent_OnDemandPath_GeneratesContent()
    {
        Guid instanceGuid = Guid.NewGuid();
        Guid dataGuid = Guid.NewGuid();
        Fixture fixture = new();
        fixture.DataElement!.BlobStoragePath = "ondemand/formdatapdf";

        DataElementContentService target = fixture.Build();
        (DataElementReadContext context, _) = await target.ResolveForRead(
            InstanceOwnerPartyId,
            instanceGuid,
            dataGuid,
            CancellationToken.None
        );

        Assert.True(context.IsOnDemandContent);

        await target.OpenContent(context, "nn", CancellationToken.None);

        fixture.OnDemandContentService.Verify(
            service =>
                service.GetContent(
                    "formdatapdf",
                    "apps-test",
                    instanceGuid,
                    dataGuid,
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
        Guid instanceGuid = Guid.NewGuid();
        Guid dataGuid = Guid.NewGuid();
        Fixture fixture = new() { A2UseTtdAsServiceOwner = useTtdAsServiceOwner };
        fixture.Instance!.Org = "digdir";
        fixture.Instance!.AppId = appId;
        fixture.DataElement!.BlobStoragePath = $"{appId}/{instanceGuid}/data/{dataGuid}";

        DataElementContentService target = fixture.Build();
        (DataElementReadContext context, _) = await target.ResolveForRead(
            InstanceOwnerPartyId,
            instanceGuid,
            dataGuid,
            CancellationToken.None
        );

        await target.OpenContent(context, "nb", CancellationToken.None);

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

        public InstanceInternal? Instance { get; init; } =
            new()
            {
                Id = Guid.NewGuid(),
                AppId = AppId,
                Org = Org,
            };

        public DataElementInternal? DataElement { get; init; } =
            new() { Id = Guid.NewGuid(), DataType = DataTypeId };

        public Application? Application { get; init; } =
            new() { DataTypes = [new DataType { Id = DataTypeId }] };

        public bool Authorized { get; init; } = true;

        public bool ActionAuthorized { get; init; } = true;

        public bool A2UseTtdAsServiceOwner { get; init; }

        public DataElementContentService Build()
        {
            Mock<IInstanceRepository> instanceRepository = new();
            instanceRepository
                .Setup(repository =>
                    repository.GetOne(
                        It.IsAny<Guid>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(Instance);

            Mock<IDataRepository> dataRepository = new();
            dataRepository
                .Setup(repository =>
                    repository.Read(
                        It.IsAny<Guid>(),
                        It.IsAny<Guid>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(DataElement!);

            Mock<IApplicationRepository> applicationRepository = new();
            applicationRepository
                .Setup(repository =>
                    repository.FindOne(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(Application);

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

            Mock<IAuthorization> authorization = new();
            authorization
                .Setup(service =>
                    service.AuthorizeEnrichedInstanceAction(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>()
                    )
                )
                .ReturnsAsync(Authorized);
            authorization
                .Setup(service =>
                    service.AuthorizeInstanceAction(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .ReturnsAsync(ActionAuthorized);

            return new DataElementContentService(
                instanceRepository.Object,
                dataRepository.Object,
                applicationRepository.Object,
                BlobRepository.Object,
                authorization.Object,
                OnDemandContentService.Object,
                Options.Create(
                    new GeneralSettings { A2UseTtdAsServiceOwner = A2UseTtdAsServiceOwner }
                )
            );
        }
    }
}
