using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
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
public class PgDataRepository(
    ILogger<PgDataRepository> logger,
    NpgsqlDataSource dataSource) : IDataRepository
{
    private readonly string _insertSql = "select * from storage.insertdataelement_v2 ($1, $2, $3, $4)";
    private readonly string _readSql = "select * from storage.readdataelement($1)";
    private readonly string _deleteSql = "select * from storage.deletedataelement_v2 ($1, $2, $3)";
    private readonly string _deleteForInstanceSql = "select * from storage.deletedataelements ($1)";
    private readonly string _updateSql = "select * from storage.updatedataelement_v2 ($1, $2, $3, $4, $5, $6)";

    private readonly ILogger<PgDataRepository> _logger = logger;
    private readonly NpgsqlDataSource _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0, CancellationToken cancellationToken = default)
    {
        dataElement.Id ??= Guid.NewGuid().ToString();
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, dataElement);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            dataElement = await reader.GetFieldValueAsync<DataElement>("updatedElement", cancellationToken: cancellationToken);
        }

        return dataElement;
    }

    /// <inheritdoc/>
    public async Task<bool> Delete(DataElement dataElement, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, dataElement.LastChangedBy);

        int rc = (int)await pgcom.ExecuteScalarAsync(cancellationToken);
        return rc == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteForInstance(string instanceId, CancellationToken cancellationToken = default)
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
            _logger.LogError(ex, "Error deleting data elements for instance {instanceId}", instanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId, CancellationToken cancellationToken = default)
    {
        DataElement dataElement = null;
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            dataElement = await reader.GetFieldValueAsync<DataElement>("element", cancellationToken: cancellationToken);
        }

        return dataElement;
    }

    /// <inheritdoc/>
    public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist, CancellationToken cancellationToken = default)
    {
        const int allowedNumberOfProperties = 14;
        if (propertylist.Count > allowedNumberOfProperties)
        {
            throw new ArgumentOutOfRangeException(nameof(propertylist), $"PropertyList can contain at most {allowedNumberOfProperties} entries.");
        }

        List<string> elementProperties = [];
        List<string> instanceProperties = [];
        DataElement element = new();
        bool isReadChangedToFalse = false;
        foreach (var kvp in propertylist)
        {
            switch (kvp.Key)
            {
                case "/locked": element.Locked = (bool)kvp.Value; elementProperties.Add(nameof(DataElement.Locked)); break;
                case "/refs": element.Refs = (List<Guid>)kvp.Value; elementProperties.Add(nameof(DataElement.Refs)); break;
                case "/references": element.References = (List<Reference>)kvp.Value; elementProperties.Add(nameof(DataElement.References)); break;
                case "/tags": element.Tags = (List<string>)kvp.Value; elementProperties.Add(nameof(DataElement.Tags)); break;
                case "/userDefinedMetadata": element.UserDefinedMetadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.UserDefinedMetadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/metadata": element.Metadata = (List<KeyValueEntry>)kvp.Value;
                    elementProperties.Add(nameof(DataElement.Metadata));
                    elementProperties.Add(nameof(KeyValueEntry.Key));
                    elementProperties.Add(nameof(KeyValueEntry.Value));
                    break;
                case "/deleteStatus": element.DeleteStatus = (DeleteStatus)kvp.Value; elementProperties.Add(nameof(DataElement.DeleteStatus)); break;
                case "/lastChanged": element.LastChanged = (DateTime?)kvp.Value; elementProperties.Add(nameof(DataElement.LastChanged)); instanceProperties.Add(nameof(Instance.LastChanged)); break;
                case "/lastChangedBy": element.LastChangedBy = (string)kvp.Value; elementProperties.Add(nameof(DataElement.LastChangedBy)); instanceProperties.Add(nameof(Instance.LastChangedBy)); break;
                case "/fileScanResult": element.FileScanResult = (FileScanResult)kvp.Value; elementProperties.Add(nameof(DataElement.FileScanResult)); break;
                case "/contentType": element.ContentType = (string)kvp.Value; elementProperties.Add(nameof(DataElement.ContentType)); break;
                case "/filename": element.Filename = (string)kvp.Value; elementProperties.Add(nameof(DataElement.Filename)); break;
                case "/size": element.Size = (long)kvp.Value; elementProperties.Add(nameof(DataElement.Size)); break;
                case "/isRead": element.IsRead = (bool)kvp.Value; elementProperties.Add(nameof(DataElement.IsRead)); isReadChangedToFalse = !element.IsRead; break;
                default: throw new ArgumentException("Unexpected key " + kvp.Key);
            }
        }

        Instance lastChangedWrapper = new() { LastChanged = element.LastChanged, LastChangedBy = element.LastChangedBy };
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);

        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, CustomSerializer.Serialize(element, elementProperties));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, CustomSerializer.Serialize(lastChangedWrapper, instanceProperties));
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, isReadChangedToFalse);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, lastChangedWrapper.LastChanged ?? (object)DBNull.Value);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            element = await reader.GetFieldValueAsync<DataElement>("updatedElement", cancellationToken: cancellationToken);
        }

        return element;
    }
}
