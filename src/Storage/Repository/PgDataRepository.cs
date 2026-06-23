#nullable disable

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IDataRepository"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PgDataRepository"/> class.
/// </remarks>
/// <param name="logger">The logger to use when writing to logs.</param>
/// <param name="dataSource">The npgsql data source.</param>
public class PgDataRepository(ILogger<PgDataRepository> logger, NpgsqlDataSource dataSource)
    : IDataRepository
{
    private readonly string _insertSql =
        "select * from storage.insertdataelement_v3 ($1, $2, $3, $4, $5)";
    private readonly string _readSql = "select * from storage.readdataelement_v2($1)";
    private readonly string _deleteSql = "select * from storage.deletedataelement_v2 ($1, $2, $3)";
    private readonly string _deleteForCleanupSql =
        "select * from storage.deletedataelementforcleanup ($1)";
    private readonly string _deleteForInstanceSql = "select * from storage.deletedataelements ($1)";
    private readonly string _updateSql =
        "select * from storage.updatedataelement_v3 ($1, $2, $3, $4, $5, $6, $7, $8, $9)";
    private readonly string _createBlobVersionSql =
        "select storage.createblobversion($1, $2, $3, $4, $5, $6)";
    private readonly string _deleteBlobVersionSql =
        "select * from storage.deleteblobversion($1, $2)";
    private readonly string _deleteOrphanBlobVersionsSql =
        "select * from storage.deleteorphanblobversions($1)";
    private readonly string _readBlobVersionsSql = "select * from storage.readblobversions($1)";
    private readonly string _existsSql = "select * from storage.readdataelementexists($1)";

    private readonly ILogger<PgDataRepository> _logger = logger;
    private readonly NpgsqlDataSource _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<DataElementInternal> Create(
        DataElementInternal dataElement,
        long instanceInternalId = 0,
        CancellationToken cancellationToken = default
    )
    {
        dataElement.DataElement.Id ??= Guid.NewGuid().ToString();
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            new Guid(dataElement.DataElement.InstanceGuid)
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.DataElement.Id));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, dataElement.DataElement);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, ToBlobVersion(dataElement.BlobVersionId));

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string result = await reader.GetFieldValueAsync<string>("result", cancellationToken);
            if (result != "ok")
            {
                throw result switch
                {
                    "not_found" => new RepositoryException(
                        $"Instance {dataElement.DataElement.InstanceGuid} was not found.",
                        HttpStatusCode.NotFound
                    ),
                    "hard_deleted" => new RepositoryException(
                        $"Instance {dataElement.DataElement.InstanceGuid} is deleted and cannot accept new data elements.",
                        HttpStatusCode.NotFound
                    ),
                    "blob_version_not_found" => new RepositoryException(
                        $"Blob version {dataElement.BlobVersionId} is not available for data element {dataElement.DataElement.Id}.",
                        HttpStatusCode.Conflict
                    ),
                    _ => new UnreachableException(
                        $"Unexpected data element create result '{result}'."
                    ),
                };
            }

            dataElement = await ReadDataElementAsync(reader, "updatedElement", cancellationToken);
            return dataElement;
        }

        throw new RepositoryException(
            $"Data element {dataElement.DataElement.Id} was not created.",
            HttpStatusCode.NotFound
        );
    }

    /// <inheritdoc/>
    public async Task<bool> Delete(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, dataElement.LastChangedBy);

        int rc = (int)await pgcom.ExecuteScalarAsync(cancellationToken);
        return rc == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteForCleanup(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteForCleanupSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));

        int rc = (int)await pgcom.ExecuteScalarAsync(cancellationToken);
        return rc == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteForInstance(
        string instanceId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteForInstanceSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId));

            await pgcom.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deleting data elements for instance {instanceId}",
                instanceId
            );
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<DataElementInternal> Read(
        Guid instanceGuid,
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        DataElementInternal dataElement = null;
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            dataElement = await ReadDataElementAsync(reader, "element", cancellationToken);
        }

        return dataElement;
    }

    /// <inheritdoc/>
    public async Task<DataElement> Update(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertylist,
        DataElementUpdateContext context = null,
        CancellationToken cancellationToken = default
    )
    {
        const int allowedNumberOfProperties = 16;
        if (propertylist.Count > allowedNumberOfProperties)
        {
            throw new ArgumentOutOfRangeException(
                nameof(propertylist),
                $"PropertyList can contain at most {allowedNumberOfProperties} entries."
            );
        }

        List<string> elementProperties = [];
        List<string> instanceProperties = [];
        DataElement element = new();
        bool isReadChangedToFalse = false;
        string blobVersionId = null;
        foreach (var kvp in propertylist)
        {
            switch (kvp.Key)
            {
                case "/locked":
                    element.Locked = (bool)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Locked));
                    break;
                case "/refs":
                    element.Refs = (List<Guid>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Refs));
                    break;
                case "/references":
                    element.References = (List<Reference>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.References));
                    break;
                case "/tags":
                    element.Tags = (List<string>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Tags));
                    break;
                case "/userDefinedMetadata":
                    element.UserDefinedMetadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.UserDefinedMetadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/metadata":
                    element.Metadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Metadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/deleteStatus":
                    element.DeleteStatus = (DeleteStatus)kvp.Value;
                    elementProperties.Add(nameof(DataElement.DeleteStatus));
                    break;
                case "/lastChanged":
                    element.LastChanged = (DateTime?)kvp.Value;
                    elementProperties.Add(nameof(DataElement.LastChanged));
                    instanceProperties.Add(nameof(Instance.LastChanged));
                    break;
                case "/lastChangedBy":
                    element.LastChangedBy = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElement.LastChangedBy));
                    instanceProperties.Add(nameof(Instance.LastChangedBy));
                    break;
                case "/fileScanResult":
                    element.FileScanResult = (FileScanResult)kvp.Value;
                    elementProperties.Add(nameof(DataElement.FileScanResult));
                    break;
                case "/contentType":
                    element.ContentType = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElement.ContentType));
                    break;
                case "/filename":
                    element.Filename = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Filename));
                    break;
                case "/size":
                    element.Size = (long)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Size));
                    break;
                case "/blobStoragePath":
                    element.BlobStoragePath = (string)kvp.Value;
                    elementProperties.Add(nameof(DataElement.BlobStoragePath));
                    break;
                case "/isRead":
                    element.IsRead = (bool)kvp.Value;
                    elementProperties.Add(nameof(DataElement.IsRead));
                    isReadChangedToFalse = !element.IsRead;
                    break;
                case "/currentBlobVersion":
                    blobVersionId = (string)kvp.Value;
                    break;
                default:
                    throw new ArgumentException("Unexpected key " + kvp.Key);
            }
        }

        context ??= new DataElementUpdateContext();

        Instance lastChangedWrapper = new()
        {
            LastChanged = element.LastChanged,
            LastChangedBy = element.LastChangedBy,
        };
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);

        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            CustomSerializer.Serialize(element, elementProperties)
        );
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            CustomSerializer.Serialize(lastChangedWrapper, instanceProperties)
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, isReadChangedToFalse);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            lastChangedWrapper.LastChanged ?? (object)DBNull.Value
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, ToBlobVersion(blobVersionId));
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            ToBlobVersion(context.ExpectedCurrentBlobVersion)
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, context.EnforceLockCheck);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string result = await reader.GetFieldValueAsync<string>("result", cancellationToken);
            if (result != "ok")
            {
                throw result switch
                {
                    "not_found" => new RepositoryException(
                        $"Data element {dataElementId} was not found.",
                        HttpStatusCode.NotFound
                    ),
                    "hard_deleted" => new RepositoryException(
                        $"Data element {dataElementId} is deleted and cannot be updated.",
                        HttpStatusCode.NotFound
                    ),
                    "locked" => new RepositoryException(
                        $"Data element {dataElementId} is locked and cannot be updated.",
                        HttpStatusCode.Conflict
                    ),
                    "version_mismatch" => new RepositoryException(
                        $"Data element {dataElementId} current blob version did not match expected version.",
                        HttpStatusCode.Conflict
                    ),
                    "blob_version_not_found" => new RepositoryException(
                        $"Blob version was not available for data element {dataElementId}.",
                        HttpStatusCode.Conflict
                    ),
                    _ => new UnreachableException(
                        $"Unexpected data element update result '{result}'."
                    ),
                };
            }

            DataElementInternal updatedElement = await ReadDataElementAsync(
                reader,
                "updatedElement",
                cancellationToken
            );
            return updatedElement.DataElement;
        }

        throw new RepositoryException(
            $"Data element {dataElementId} was not found.",
            HttpStatusCode.NotFound
        );
    }

    /// <inheritdoc/>
    public async Task<DataElement> UpdateFileScanStatus(
        Guid instanceGuid,
        Guid dataElementId,
        FileScanStatus fileScanStatus,
        CancellationToken cancellationToken = default
    )
    {
        DataElement element = new() { FileScanResult = fileScanStatus.FileScanResult };
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            CustomSerializer.Serialize(element, [nameof(DataElement.FileScanResult)])
        );
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            CustomSerializer.Serialize(new Instance(), [])
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, false);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, DBNull.Value);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, DBNull.Value);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            ToBlobVersion(fileScanStatus.BlobVersionId)
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, false);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string result = await reader.GetFieldValueAsync<string>("result", cancellationToken);
            if (result is "not_found" or "hard_deleted" or "version_mismatch")
            {
                return null;
            }

            if (result != "ok")
            {
                throw new UnreachableException(
                    $"Unexpected file scan status update result '{result}'."
                );
            }

            DataElementInternal updatedElement = await ReadDataElementAsync(
                reader,
                "updatedElement",
                cancellationToken
            );
            return updatedElement.DataElement;
        }

        return null;
    }

    private static object ToBlobVersion(string blobVersionId)
    {
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return DBNull.Value;
        }

        try
        {
            return BlobVersionId.Decode(blobVersionId);
        }
        catch (FormatException exception)
        {
            throw new RepositoryException(
                $"Blob version id '{blobVersionId}' is not valid.",
                exception,
                HttpStatusCode.BadRequest
            );
        }
    }

    private static async Task<DataElementInternal> ReadDataElementAsync(
        NpgsqlDataReader reader,
        string elementColumn,
        CancellationToken cancellationToken
    )
    {
        DataElement dataElement = await reader.GetFieldValueAsync<DataElement>(
            elementColumn,
            cancellationToken: cancellationToken
        );
        int versionOrdinal = reader.GetOrdinal("currentblobversion");
        string blobVersionId = await reader.IsDBNullAsync(versionOrdinal, cancellationToken)
            ? null
            : BlobVersionId.Encode(
                await reader.GetFieldValueAsync<Guid>(
                    versionOrdinal,
                    cancellationToken: cancellationToken
                )
            );
        return new DataElementInternal(dataElement, blobVersionId);
    }

    /// <inheritdoc/>
    public async Task<string> CreateBlobVersionId(
        Guid instanceGuid,
        Guid dataElementId,
        string appId,
        string blobStorageOrg,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        Guid version = Guid.CreateVersion7();

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_createBlobVersionSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, version);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, appId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, blobStorageOrg);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Integer,
            storageAccountNumber ?? (object)DBNull.Value
        );

        await pgcom.ExecuteNonQueryAsync(cancellationToken);
        return BlobVersionId.Encode(version);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        List<BlobVersionReferencesInternal> blobVersions = [];
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readBlobVersionsSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            blobVersions.Add(
                await ReadBlobVersionReferencesAsync(
                    reader,
                    "instanceguid",
                    "appid",
                    "blobstorageorg",
                    "storageaccountnumber",
                    "blobversions",
                    cancellationToken
                )
            );
        }

        return blobVersions;
    }

    internal static async Task<BlobVersionReferencesInternal> ReadBlobVersionReferencesAsync(
        NpgsqlDataReader reader,
        string instanceGuidColumn,
        string appIdColumn,
        string blobStorageOrgColumn,
        string storageAccountNumberColumn,
        string blobVersionsColumn,
        CancellationToken cancellationToken
    )
    {
        int storageAccountOrdinal = reader.GetOrdinal(storageAccountNumberColumn);
        int? storageAccountNumber = await reader.IsDBNullAsync(
            storageAccountOrdinal,
            cancellationToken
        )
            ? null
            : await reader.GetFieldValueAsync<int>(storageAccountOrdinal, cancellationToken);
        Guid[] blobVersions = await reader.GetFieldValueAsync<Guid[]>(
            blobVersionsColumn,
            cancellationToken
        );

        return new BlobVersionReferencesInternal(
            await reader.GetFieldValueAsync<Guid>(instanceGuidColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(appIdColumn, cancellationToken),
            await reader.GetFieldValueAsync<string>(blobStorageOrgColumn, cancellationToken),
            storageAccountNumber,
            blobVersions.Select(BlobVersionId.Encode)
        );
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBlobVersion(
        Guid dataElementId,
        string blobVersionId,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteBlobVersionSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, ToBlobVersion(blobVersionId));

        int rc = (int)await pgcom.ExecuteScalarAsync(cancellationToken);
        return rc == 1;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOrphanBlobVersions(
        IReadOnlyList<string> blobVersionIds,
        CancellationToken cancellationToken = default
    )
    {
        Guid[] versions =
            blobVersionIds
                ?.Where(versionId => !string.IsNullOrEmpty(versionId))
                .Select(BlobVersionId.Decode)
                .ToArray()
            ?? [];

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteOrphanBlobVersionsSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Uuid, versions);

        return (int)await pgcom.ExecuteScalarAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> Exists(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    )
    {
        bool? result = null;
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_existsSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            result = await reader.GetFieldValueAsync<bool>("element", cancellationToken);
        }

        if (result.HasValue)
        {
            return result.Value;
        }
        else
        {
            // This code path should never execute if the expected result is returned
            InvalidOperationException exception = new(
                $"Unexpected return value from: {nameof(Exists)}"
            );
            _logger.LogError(
                exception,
                "Unexpected state while checking if data element exists. DataElementId: {DataElementId}",
                dataElementId
            );
            throw exception;
        }
    }
}
