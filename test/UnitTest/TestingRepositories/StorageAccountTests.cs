#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

/// <summary>
/// Storage account rows are never deleted by tests, so every test uses its own org to keep
/// writable rows from routing writes in other tests.
/// </summary>
[Collection("StoragePostgreSQL")]
public class StorageAccountTests(DataElementFixture fixture)
    : IClassFixture<DataElementFixture>,
        IAsyncLifetime
{
    private const string _dataElementTemplate1 = "cdb627fd-c586-41f5-99db-bae38daa2b59";
    private const string _dataElementTemplate2 = "5ebeb498-677d-476f-8cab-b788a0fd0640";

    private readonly PgStorageAccountRepository _repository = new(fixture.DataSource);
    private readonly string _org = $"sa{Guid.NewGuid():N}";
    private InstanceInternal _instance;

    public async Task InitializeAsync()
    {
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Id = $"{instance.InstanceOwner.PartyId}/{Guid.NewGuid()}";
        InstanceInternal created = await fixture.InstanceRepo.Create(
            instance.FromApiModel(),
            CancellationToken.None
        );
        _instance = await fixture.InstanceRepo.GetOne(created.Id, false, CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_ReturnsIncreasingIds()
    {
        short first = await _repository.Insert(Azure("first"), _org, 1, writable: false);
        short second = await _repository.Insert(Azure("second"), _org, 1, writable: false);

        Assert.True(second > first);
    }

    [Fact]
    public async Task ReadAll_ReturnsDocumentsAsTheirVariant()
    {
        AzureBlobProviderInfo azure = new(
            "devstoreaccount1",
            "named-container",
            new Uri("http://127.0.0.1:10000/devstoreaccount1")
        );
        FileSystemProviderInfo fileSystem = new("/data/blobs/ttd");
        short azureId = await _repository.Insert(azure, _org, 1, writable: true);
        short fileSystemId = await _repository.Insert(fileSystem, _org, 2, writable: false);

        IReadOnlyList<StorageAccountRow> rows = (await _repository.ReadAll()).Readable;

        StorageAccountRow azureRow = Assert.Single(rows, row => row.Id == azureId);
        Assert.Equal(azure, azureRow.ProviderInfo);
        Assert.Equal(_org, azureRow.Org);
        Assert.Equal(1, azureRow.StorageAccountNumber);
        Assert.True(azureRow.Writable);
        StorageAccountRow fileSystemRow = Assert.Single(rows, row => row.Id == fileSystemId);
        Assert.Equal(fileSystem, fileSystemRow.ProviderInfo);
        Assert.Equal(2, fileSystemRow.StorageAccountNumber);
        Assert.False(fileSystemRow.Writable);
        Assert.Equal(StorageAccountProviderInfo.AzureBlob, azureRow.Provider);
        Assert.Equal(StorageAccountProviderInfo.FileSystem, fileSystemRow.Provider);
        Assert.Equal(
            StorageAccountProviderInfo.FileSystem,
            await PostgresUtil.RunQuery<string>(
                $"select provider from storage.storageaccounts where id = {fileSystemId}"
            )
        );
        Assert.Equal(
            "/data/blobs/ttd",
            await PostgresUtil.RunQuery<string>(
                $"select providerinfo->>'directory' from storage.storageaccounts where id = {fileSystemId}"
            )
        );
        Assert.False(
            await PostgresUtil.RunQuery<bool>(
                $"select bool_or(jsonb_exists(providerinfo, 'provider')) from storage.storageaccounts where id in ({azureId}, {fileSystemId})"
            )
        );
    }

    [Fact]
    public async Task Insert_Writable_TakesFlagOverFromPreviousWritableRow()
    {
        short first = await _repository.Insert(Azure("first"), _org, 1, writable: true);
        short otherNumber = await _repository.Insert(Azure("other"), _org, 2, writable: true);

        short second = await _repository.Insert(Azure("second"), _org, 1, writable: true);
        short readOnly = await _repository.Insert(Azure("readonly"), _org, 1, writable: false);

        IReadOnlyList<StorageAccountRow> rows = (await _repository.ReadAll()).Readable;
        Assert.False(rows.Single(row => row.Id == first).Writable);
        Assert.True(rows.Single(row => row.Id == second).Writable);
        Assert.False(rows.Single(row => row.Id == readOnly).Writable);
        Assert.True(rows.Single(row => row.Id == otherNumber).Writable);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresUtil.RunSql(
                $"update storage.storageaccounts set writable = true where id = {first}"
            )
        );
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Theory]
    [InlineData("unknown-provider", """{"accountName":"a","container":"c"}""")]
    [InlineData(StorageAccountProviderInfo.AzureBlob, """{"accountName":"a"}""")]
    public async Task ReadAll_UnreadableRow_ReturnsItAsUnreadableBesideReadableRows(
        string provider,
        string document
    )
    {
        short readableId = await _repository.Insert(Azure("readable"), _org, 2, writable: true);
        short id = await PostgresUtil.RunQuery<short>(
            $"select storage.insertstorageaccount('{provider}', '{document}'::jsonb, '{_org}', 1, true)"
        );

        try
        {
            StorageAccountRows rows = await _repository.ReadAll();

            Assert.Single(rows.Readable, row => row.Id == readableId);
            Assert.DoesNotContain(rows.Readable, row => row.Id == id);
            UnreadableStorageAccountRow unreadable = Assert.Single(
                rows.Unreadable,
                row => row.Id == id
            );
            Assert.Equal(provider, unreadable.Provider);
            Assert.Equal(_org, unreadable.Org);
            Assert.Equal(1, unreadable.StorageAccountNumber);
            Assert.True(unreadable.Writable);
            Assert.False(string.IsNullOrEmpty(unreadable.Error));
            Assert.StartsWith($"Storage account {id} ", unreadable.Description);
            Assert.Contains($"'{provider}'", unreadable.Description);
        }
        finally
        {
            await PostgresUtil.RunSql($"delete from storage.storageaccounts where id = {id}");
        }
    }

    [Fact]
    public async Task StartupCheck_UnreadableRow_LogsCriticalAndSucceeds()
    {
        short id = await PostgresUtil.RunQuery<short>(
            $$"""select storage.insertstorageaccount('unknown-provider', '{"accountName":"a","container":"c"}'::jsonb, '{{_org}}', 1, false)"""
        );
        CapturingLoggerProvider logs = new();
        StorageAccountStartupCheck startupCheck = new(
            _repository,
            logs.CreateLogger<StorageAccountStartupCheck>()
        );

        try
        {
            await startupCheck.StartAsync(CancellationToken.None);

            string critical = Assert.Single(
                logs.Messages(LogLevel.Critical),
                message => message.StartsWith($"Storage account {id} ")
            );
            Assert.Contains("'unknown-provider'", critical);
        }
        finally
        {
            await PostgresUtil.RunSql($"delete from storage.storageaccounts where id = {id}");
        }
    }

    [Fact]
    public async Task StartupCheck_ReadableRows_Succeeds()
    {
        await _repository.Insert(Azure("startup"), _org, 1, writable: false);
        StorageAccountStartupCheck startupCheck = new(
            _repository,
            NullLogger<StorageAccountStartupCheck>.Instance
        );

        await startupCheck.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CreateBlobVersionId_UnknownStorageAccountId_ViolatesForeignKey()
    {
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.DataRepo.CreateBlobVersionId(
                _instance.Id,
                Guid.NewGuid(),
                _instance.AppId,
                _instance.Org,
                1,
                short.MaxValue
            )
        );

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task CreateBlobVersionId_RecordsStorageAccountIdWhenGiven()
    {
        short accountId = await _repository.Insert(Azure("named"), _org, 3, writable: false);
        Guid dataElementId = Guid.NewGuid();

        string withId = await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            dataElementId,
            _instance.AppId,
            _instance.Org,
            3,
            accountId
        );
        string withoutId = await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            dataElementId,
            _instance.AppId,
            _instance.Org,
            1,
            null
        );

        DataElementBlobVersion withIdRow = await fixture.DataRepo.ReadBlobVersion(
            _instance.Id,
            dataElementId,
            withId
        );
        DataElementBlobVersion withoutIdRow = await fixture.DataRepo.ReadBlobVersion(
            _instance.Id,
            dataElementId,
            withoutId
        );
        Assert.Equal(accountId, withIdRow.StorageAccountId);
        Assert.Equal(3, withIdRow.StorageAccountNumber);
        Assert.Null(withoutIdRow.StorageAccountId);
        Assert.Equal(1, withoutIdRow.StorageAccountNumber);
        IReadOnlyList<BlobVersionReferencesInternal> detached =
            await fixture.DataRepo.ReadDetachedBlobVersions(dataElementId);
        Assert.Equal(2, detached.Count);
        Assert.Contains(detached, refs => refs.StorageAccountId == accountId);
        Assert.Contains(detached, refs => refs.StorageAccountId is null);
    }

    [Fact]
    public async Task Readers_ReturnStorageLocationOfCurrentBlobVersion()
    {
        short accountId = await _repository.Insert(Azure("named"), _org, 4, writable: false);
        DataElementInternal legacy = await CreateLegacyElement();
        DataElementInternal versioned = await CreateVersionedElement(4, accountId);

        DataElementInternal readVersioned = await fixture.DataRepo.Read(_instance.Id, versioned.Id);
        DataElementInternal readLegacy = await fixture.DataRepo.Read(_instance.Id, legacy.Id);
        InstanceInternal instance = await fixture.InstanceRepo.GetOne(
            _instance.Id,
            true,
            CancellationToken.None
        );

        AssertLocation(readVersioned, accountId, _instance.Org, 4);
        AssertLegacy(readLegacy);
        AssertLocation(
            instance.Data.Single(d => d.Id == versioned.Id),
            accountId,
            _instance.Org,
            4
        );
        AssertLegacy(instance.Data.Single(d => d.Id == legacy.Id));
        IReadOnlyList<BlobVersionReferencesInternal> attached =
            await fixture.DataRepo.ReadBlobVersions(_instance.Id, versioned.Id);
        Assert.Equal(accountId, Assert.Single(attached).StorageAccountId);
        BlobVersionReferencesInternal forInstance = Assert.Single(
            await fixture.InstanceRepo.GetBlobVersionsForInstance(
                _instance.Id,
                CancellationToken.None
            )
        );
        Assert.Equal(accountId, forInstance.StorageAccountId);
    }

    [Fact]
    public async Task ReadDataElement_VersionWithoutStorageAccountId_ReturnsOrgAndNumber()
    {
        DataElementInternal versioned = await CreateVersionedElement(6, null);

        DataElementInternal read = await fixture.DataRepo.Read(_instance.Id, versioned.Id);

        AssertLocation(read, null, _instance.Org, 6);
    }

    [Fact]
    public async Task CreateBlobVersionId_RecordsStorageAccountNumberAsGiven()
    {
        DataElementInternal withoutNumber = await CreateVersionedElement(null, null);
        DataElementInternal withNumber = await CreateVersionedElement(3, null);

        DataElementInternal readWithoutNumber = await fixture.DataRepo.Read(
            _instance.Id,
            withoutNumber.Id
        );
        DataElementInternal readWithNumber = await fixture.DataRepo.Read(
            _instance.Id,
            withNumber.Id
        );

        AssertLocation(readWithoutNumber, null, _instance.Org, null);
        AssertLocation(readWithNumber, null, _instance.Org, 3);
        DataElementBlobVersion row = await fixture.DataRepo.ReadBlobVersion(
            _instance.Id,
            withoutNumber.Id,
            readWithoutNumber.BlobVersionId
        );
        Assert.Null(row.StorageAccountNumber);
        StorageAccountResolver resolver = new(
            _repository,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(
                new AzureStorageConfiguration
                {
                    AccountName = "altinnstorage",
                    OrgStorageAccount = "{0}altinndevstorage01",
                    OrgStorageContainer = "{0}-dev-appsdata-blob-db",
                }
            ),
            Options.Create(new StorageAccountSettings()),
            NullLogger<StorageAccountResolver>.Instance
        );
        Assert.Equal(
            resolver.PatternDefault(
                _instance.Org,
                StorageAccountNaming.DefaultStorageAccountNumber
            ),
            await resolver.ForElement(readWithoutNumber, _instance.Org, 3)
        );
        Assert.Equal(
            resolver.PatternDefault(_instance.Org, 3),
            await resolver.ForElement(readWithNumber, _instance.Org, 3)
        );
    }

    [Fact]
    public async Task ApplyInstanceMutation_ReturnsStorageLocationOfCurrentBlobVersion()
    {
        short accountId = await _repository.Insert(Azure("named"), _org, 5, writable: false);
        DataElementInternal legacy = await CreateLegacyElement();
        DataElementInternal toCreate = NewElement(_dataElementTemplate2);
        toCreate.BlobVersionId = await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            toCreate.Id,
            _instance.AppId,
            _instance.Org,
            5,
            accountId
        );
        toCreate.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            _instance.Id,
            toCreate.BlobVersionId
        );
        InstanceMutationCommit mutation = new(
            [toCreate],
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            await ReadInstanceVersion(),
            null,
            [],
            Guid.NewGuid()
        );

        InstanceMutationApplyResult result = await fixture.InstanceMutationRepo.Apply(
            _instance.Id,
            _instance.InternalId,
            mutation
        );

        AssertLocation(
            result.Instance.Data.Single(d => d.Id == toCreate.Id),
            accountId,
            _instance.Org,
            5
        );
        AssertLegacy(result.Instance.Data.Single(d => d.Id == legacy.Id));
    }

    [Fact]
    public async Task GetOrphanBlobVersionsForCleanup_ReturnsStorageAccountIdPerGroup()
    {
        short accountId = await _repository.Insert(Azure("orphan"), _org, 1, writable: false);
        Guid dataElementId = Guid.NewGuid();
        string withId;
        string withoutId;
        await PostgresUtil.FreezeTime(DateTimeOffset.UtcNow.AddDays(-8));
        try
        {
            withId = await fixture.DataRepo.CreateBlobVersionId(
                _instance.Id,
                dataElementId,
                _instance.AppId,
                _instance.Org,
                1,
                accountId
            );
            withoutId = await fixture.DataRepo.CreateBlobVersionId(
                _instance.Id,
                dataElementId,
                _instance.AppId,
                _instance.Org,
                1,
                null
            );
        }
        finally
        {
            await PostgresUtil.UnfreezeTime();
        }

        List<BlobVersionReferencesInternal> orphans =
            await fixture.InstanceRepo.GetOrphanBlobVersionsForCleanup(CancellationToken.None);

        List<BlobVersionReferencesInternal> ours =
        [
            .. orphans.Where(refs => refs.InstanceGuid == _instance.Id),
        ];
        Assert.Equal(2, ours.Count);
        Assert.Equal(
            [withId],
            Assert.Single(ours, refs => refs.StorageAccountId == accountId).BlobVersionIds
        );
        Assert.Equal(
            [withoutId],
            Assert.Single(ours, refs => refs.StorageAccountId is null).BlobVersionIds
        );
    }

    [Fact]
    public async Task GetHardDeletedDataElements_ReturnsStorageAccountIdOfAttachedVersions()
    {
        short accountId = await _repository.Insert(Azure("harddeleted"), _org, 1, writable: false);
        DataElementInternal element = await CreateHardDeletedElement(accountId);

        List<DeletedDataElementInternal> deleted =
            await fixture.InstanceRepo.GetHardDeletedDataElements(CancellationToken.None);

        DeletedDataElementInternal ours = Assert.Single(
            deleted,
            candidate => candidate.DataElement.Id == element.Id
        );
        BlobVersionReferencesInternal refs = Assert.Single(ours.BlobVersions);
        Assert.Equal(accountId, refs.StorageAccountId);
        Assert.Equal([element.BlobVersionId], refs.BlobVersionIds);
    }

    [Fact]
    public async Task ReadDetachedBlobVersionsV1_SkipsVersionsInNamedAccounts()
    {
        short accountId = await _repository.Insert(Azure("detachedv1"), _org, 1, writable: false);
        Guid dataElementId = Guid.NewGuid();
        await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            dataElementId,
            _instance.AppId,
            _instance.Org,
            1,
            accountId
        );
        string withoutId = await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            dataElementId,
            _instance.AppId,
            _instance.Org,
            1,
            null
        );

        List<Guid> versions = await ReadGuids(
            "select unnest(blobversions) from storage.readdetachedblobversions($1)",
            dataElementId
        );

        Assert.Equal([BlobVersionId.Decode(withoutId)], versions);
    }

    [Fact]
    public async Task ReadOrphanBlobVersionsForCleanupV1_SkipsVersionsInNamedAccounts()
    {
        short accountId = await _repository.Insert(Azure("orphanv1"), _org, 1, writable: false);
        Guid dataElementId = Guid.NewGuid();
        string withId;
        string withoutId;
        await PostgresUtil.FreezeTime(DateTimeOffset.UtcNow.AddDays(-8));
        try
        {
            withId = await fixture.DataRepo.CreateBlobVersionId(
                _instance.Id,
                dataElementId,
                _instance.AppId,
                _instance.Org,
                1,
                accountId
            );
            withoutId = await fixture.DataRepo.CreateBlobVersionId(
                _instance.Id,
                dataElementId,
                _instance.AppId,
                _instance.Org,
                1,
                null
            );
        }
        finally
        {
            await PostgresUtil.UnfreezeTime();
        }

        List<Guid> versions = await ReadGuids(
            "select unnest(blobversions) from storage.readorphanblobversionsforcleanup() where instanceguid = $1",
            _instance.Id
        );

        Assert.Equal([BlobVersionId.Decode(withoutId)], versions);
        Assert.DoesNotContain(BlobVersionId.Decode(withId), versions);
    }

    [Fact]
    public async Task ReadHardDeletedDataElementsForCleanupV1_SkipsElementsWithVersionInNamedAccount()
    {
        short accountId = await _repository.Insert(
            Azure("harddeletedv1"),
            _org,
            1,
            writable: false
        );
        DataElementInternal named = await CreateHardDeletedElement(accountId);
        DataElementInternal pattern = await CreateHardDeletedElement(null);

        List<Guid> elements = await ReadGuids(
            "select (element->>'Id')::uuid from storage.readharddeleteddataelementsforcleanup() where (element->>'Id')::uuid = any($1)",
            new[] { named.Id, pattern.Id }
        );

        Assert.Equal([pattern.Id], elements);
    }

    private static AzureBlobProviderInfo Azure(string container) => new("account", container);

    private static void AssertLocation(
        DataElementInternal element,
        short? storageAccountId,
        string org,
        int? number
    )
    {
        Assert.NotNull(element.BlobVersionId);
        Assert.True(element.BlobStorageLoaded);
        Assert.Equal(storageAccountId, element.StorageAccountId);
        Assert.Equal(org, element.BlobStorageOrg);
        Assert.Equal(number, element.BlobStorageAccountNumber);
    }

    private static void AssertLegacy(DataElementInternal element)
    {
        Assert.Null(element.BlobVersionId);
        Assert.True(element.BlobStorageLoaded);
        Assert.Null(element.StorageAccountId);
        Assert.Null(element.BlobStorageOrg);
        Assert.Null(element.BlobStorageAccountNumber);
    }

    private DataElementInternal NewElement(string template)
    {
        DataElement dataElement = TestDataUtil.GetDataElement(template);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = _instance.Id.ToString();
        dataElement.Created ??= DateTime.UtcNow;
        dataElement.CreatedBy ??= "1337";
        dataElement.LastChanged ??= DateTime.UtcNow;
        dataElement.LastChangedBy ??= "1337";
        return dataElement.FromApiModel();
    }

    private async Task<DataElementInternal> CreateLegacyElement()
    {
        DataElementWriteResult result = await fixture.DataRepo.Create(
            NewElement(_dataElementTemplate1),
            _instance.InternalId
        );
        return result.DataElement;
    }

    private async Task<DataElementInternal> CreateVersionedElement(
        int? storageAccountNumber,
        short? storageAccountId
    )
    {
        DataElementInternal element = NewElement(_dataElementTemplate2);
        element.BlobVersionId = await fixture.DataRepo.CreateBlobVersionId(
            _instance.Id,
            element.Id,
            _instance.AppId,
            _instance.Org,
            storageAccountNumber,
            storageAccountId
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            _instance.Id,
            element.BlobVersionId
        );
        DataElementWriteResult result = await fixture.DataRepo.Create(
            element,
            _instance.InternalId
        );
        return result.DataElement;
    }

    private async Task<DataElementInternal> CreateHardDeletedElement(short? storageAccountId)
    {
        Instance apiInstance = TestData.Instance_1_1.Clone();
        apiInstance.Id = $"{apiInstance.InstanceOwner.PartyId}/{Guid.NewGuid()}";
        InstanceInternal instance = apiInstance.FromApiModel();
        instance.CompleteConfirmations =
        [
            new CompleteConfirmation
            {
                StakeholderId = instance.Org,
                ConfirmedOn = DateTime.UtcNow.AddDays(-8),
            },
        ];
        instance = await fixture.InstanceRepo.Create(instance, CancellationToken.None);
        instance = await fixture.InstanceRepo.GetOne(instance.Id, false, CancellationToken.None);
        DataElementInternal element = NewElement(_dataElementTemplate2);
        element.InstanceGuid = instance.Id;
        element.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow.AddDays(-8),
        };
        element.BlobVersionId = await fixture.DataRepo.CreateBlobVersionId(
            instance.Id,
            element.Id,
            instance.AppId,
            instance.Org,
            1,
            storageAccountId
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            instance.AppId,
            instance.Id,
            element.BlobVersionId
        );
        await fixture.DataRepo.Create(element, instance.InternalId);
        return element;
    }

    private async Task<List<Guid>> ReadGuids(string sql, object parameter)
    {
        List<Guid> ids = [];
        await using NpgsqlCommand command = fixture.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameter);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private Task<int> ReadInstanceVersion() =>
        PostgresUtil.RunQuery<int>(
            $"select instance_version from storage.instances where alternateid = '{_instance.Id}'"
        );
}
