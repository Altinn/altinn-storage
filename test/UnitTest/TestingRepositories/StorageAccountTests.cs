#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
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

        IReadOnlyList<StorageAccountRow> rows = await _repository.ReadAll();

        StorageAccountRow azureRow = Assert.Single(rows, row => row.Id == azureId);
        Assert.Equal(azure, azureRow.ProviderInfo);
        Assert.Equal(_org, azureRow.Org);
        Assert.Equal(1, azureRow.StorageAccountNumber);
        Assert.True(azureRow.Writable);
        StorageAccountRow fileSystemRow = Assert.Single(rows, row => row.Id == fileSystemId);
        Assert.Equal(fileSystem, fileSystemRow.ProviderInfo);
        Assert.Equal(2, fileSystemRow.StorageAccountNumber);
        Assert.False(fileSystemRow.Writable);
        Assert.Equal(
            "file-system",
            await PostgresUtil.RunQuery<string>(
                $"select providerinfo->>'provider' from storage.storageaccounts where id = {fileSystemId}"
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

        IReadOnlyList<StorageAccountRow> rows = await _repository.ReadAll();
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

    [Fact]
    public async Task ReadAll_UnknownProvider_ThrowsNamingRow()
    {
        short id = await PostgresUtil.RunQuery<short>(
            $"select storage.insertstorageaccount('{{\"provider\":\"unknown-provider\"}}'::jsonb, '{_org}', 1, false)"
        );

        try
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.ReadAll());
            Assert.Contains($"Storage account {id}", exception.Message);
        }
        finally
        {
            // The row would break every later read of the table.
            await PostgresUtil.RunSql($"delete from storage.storageaccounts where id = {id}");
        }
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

    private static AzureBlobProviderInfo Azure(string container) => new("account", container);

    private static void AssertLocation(
        DataElementInternal element,
        short? storageAccountId,
        string org,
        int number
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
        int storageAccountNumber,
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

    private Task<int> ReadInstanceVersion() =>
        PostgresUtil.RunQuery<int>(
            $"select instance_version from storage.instances where alternateid = '{_instance.Id}'"
        );
}
