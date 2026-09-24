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
    public async Task ResolveForReadForUser_AuthorizesInstanceAndDataTypeForTheUser()
    {
        UserSubject subject = new(20001337, 3);
        Fixture fixture = new()
        {
            Application = new Application
            {
                DataTypes = [new DataType { Id = DataTypeId, ActionRequiredToRead = "sign" }],
            },
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForReadForUser(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                subject,
                CancellationToken.None
            );

        Assert.NotNull(context);
        Assert.Null(serviceError);
        fixture.Authorization.Verify(
            service =>
                service.AuthorizeEnrichedInstanceActionForUser(
                    It.IsAny<InstanceInternal>(),
                    "read",
                    subject
                ),
            Times.Once
        );
        fixture.Authorization.Verify(
            service =>
                service.AuthorizeInstanceActionForUser(
                    It.IsAny<InstanceInternal>(),
                    "sign",
                    It.IsAny<string>(),
                    subject
                ),
            Times.Once
        );
        fixture.Authorization.Verify(
            service =>
                service.AuthorizeEnrichedInstanceAction(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
        fixture.Authorization.Verify(
            service =>
                service.AuthorizeInstanceAction(
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }

    [Theory]
    [InlineData("12346", false)]
    [InlineData(null, false)]
    [InlineData("12345", true)]
    public async Task ResolveForReadForUser_OtherPartyOrHardDeletedInstance_ReturnsNotFoundBeforeAuthorization(
        string? instanceOwnerPartyId,
        bool isHardDeleted
    )
    {
        Guid instanceGuid = Guid.NewGuid();
        Fixture fixture = new()
        {
            Instance = new InstanceInternal
            {
                Id = instanceGuid,
                AppId = AppId,
                Org = Org,
                InstanceOwner = new InstanceOwner { PartyId = instanceOwnerPartyId },
                Status = new InstanceStatus { IsHardDeleted = isHardDeleted },
            },
            Authorized = false,
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForReadForUser(
                InstanceOwnerPartyId,
                instanceGuid,
                Guid.NewGuid(),
                new UserSubject(20001337, 3),
                CancellationToken.None
            );

        Assert.Null(context);
        Assert.Equal(404, serviceError.ErrorCode);
        Assert.Equal(
            $"Unable to find any instance with id: {InstanceOwnerPartyId}/{instanceGuid}.",
            serviceError.ErrorMessage
        );
        fixture.Authorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveForRead_InstanceOfOtherParty_IsResolved()
    {
        Fixture fixture = new()
        {
            Instance = new InstanceInternal
            {
                Id = Guid.NewGuid(),
                AppId = AppId,
                Org = Org,
                InstanceOwner = new InstanceOwner { PartyId = "12346" },
                Status = new InstanceStatus { IsHardDeleted = true },
            },
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForRead(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None
            );

        Assert.NotNull(context);
        Assert.Null(serviceError);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ResolveForReadForUser_UserNotAuthorized_ReturnsForbidden(
        bool instanceReadAuthorized,
        bool dataTypeReadAuthorized
    )
    {
        Fixture fixture = new()
        {
            Application = new Application
            {
                DataTypes = [new DataType { Id = DataTypeId, ActionRequiredToRead = "sign" }],
            },
            Authorized = instanceReadAuthorized,
            ActionAuthorized = dataTypeReadAuthorized,
        };

        (DataElementReadContext context, ServiceError serviceError) = await fixture
            .Build()
            .ResolveForReadForUser(
                InstanceOwnerPartyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new UserSubject(20001337, 3),
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
                InstanceOwner = new InstanceOwner { PartyId = InstanceOwnerPartyId.ToString() },
            };

        public DataElementInternal? DataElement { get; init; } =
            new() { Id = Guid.NewGuid(), DataType = DataTypeId };

        public Application? Application { get; init; } =
            new() { DataTypes = [new DataType { Id = DataTypeId }] };

        public bool Authorized { get; init; } = true;

        public bool ActionAuthorized { get; init; } = true;

        public bool A2UseTtdAsServiceOwner { get; init; }

        public Mock<IAuthorization> Authorization { get; } = new();

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

            Authorization
                .Setup(service =>
                    service.AuthorizeEnrichedInstanceAction(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>()
                    )
                )
                .ReturnsAsync(Authorized);
            Authorization
                .Setup(service =>
                    service.AuthorizeInstanceAction(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .ReturnsAsync(ActionAuthorized);
            Authorization
                .Setup(service =>
                    service.AuthorizeEnrichedInstanceActionForUser(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>(),
                        It.IsAny<UserSubject>()
                    )
                )
                .ReturnsAsync(Authorized);
            Authorization
                .Setup(service =>
                    service.AuthorizeInstanceActionForUser(
                        It.IsAny<InstanceInternal>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<UserSubject>()
                    )
                )
                .ReturnsAsync(ActionAuthorized);

            return new DataElementContentService(
                instanceRepository.Object,
                dataRepository.Object,
                applicationRepository.Object,
                BlobRepository.Object,
                Authorization.Object,
                OnDemandContentService.Object,
                Options.Create(
                    new GeneralSettings { A2UseTtdAsServiceOwner = A2UseTtdAsServiceOwner }
                )
            );
        }
    }
}
