using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;

using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IDataRepository"/>.
    /// </summary>
    public class PgDataRepository : IDataRepository
    {
        private readonly string _insertSql = "call storage.insertdataelement ($1, $2, $3, $4)";
        private readonly string _readAllSql = "select * from storage.readalldataelement($1)";
        private readonly string _readAllForMultipleSql = "select * from storage.readallformultipledataelement($1)";
        private readonly string _readSql = "select * from storage.readdataelement($1)";
        private readonly string _deleteSql = "select * from storage.deletedataelement ($1)";
        private readonly string _deleteForInstanceSql = "select * from storage.deletedataelements ($1)";
        private readonly string _updateSql = "call storage.updatedataelement ($1, $2)";

        private readonly ILogger<PgDataRepository> _logger;
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="PgDataRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="dataSource">The npgsql data source.</param>
        public PgDataRepository(
            ILogger<PgDataRepository> logger,
            NpgsqlDataSource dataSource)
        {
            _logger = logger;
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0)
        {
            dataElement.Id ??= Guid.NewGuid().ToString();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, dataElement);

            await pgcom.ExecuteNonQueryAsync();

            return dataElement;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));

            return (int)await pgcom.ExecuteScalarAsync() == 1;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteForInstance(string instanceId)
        {
            try
            {
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteForInstanceSql);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId));

                await pgcom.ExecuteScalarAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting instances for {instanceId}, {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<DataElement>("element");
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            List<DataElement> elements = new();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readAllSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    elements.Add(reader.GetFieldValue<DataElement>("element"));
                }
            }

            return elements;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids)
        {
            ////TODO: Remove this method/interface and join the dataelements at the inestance level
            //// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            Dictionary<string, List<DataElement>> dataElements = new();
            if (instanceGuids == null || instanceGuids.Count == 0)
            {
                return dataElements;
            }

            foreach (var guidString in instanceGuids)
            {
                dataElements[guidString] = new List<DataElement>();
            }

            List<Guid> instanceGuidsAsGuids = new();
            foreach (var instance in instanceGuids)
            {
                instanceGuidsAsGuids.Add(new Guid(instance));
            }

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readAllForMultipleSql);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Uuid, instanceGuidsAsGuids ?? (object)DBNull.Value);

            await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    DataElement element = reader.GetFieldValue<DataElement>("element");
                    if (!dataElements.ContainsKey(element.InstanceGuid))
                    {
                        dataElements.Add(element.InstanceGuid, new List<DataElement>());
                    }

                    dataElements[element.InstanceGuid].Add(element);
                }
            }

            return dataElements;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist)
        {
            if (propertylist.Count > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(propertylist), "PropertyList can contain at most 10 entries.");
            }

            await using var transConnection = _dataSource.CreateConnection();
            await transConnection.OpenAsync();
            await using var transaction = await transConnection.BeginTransactionAsync(isolationLevel: IsolationLevel.RepeatableRead); // Ensure that the read element is locked until updated
            DataElement element = await Read(Guid.Empty, dataElementId) ?? throw new ArgumentException("Element not found for id " + dataElementId, nameof(dataElementId));

            foreach (var kvp in propertylist)
            {
                switch (kvp.Key)
                {
                    case "/locked": element.Locked = (bool)kvp.Value; break;
                    case "/refs": element.Refs = (List<Guid>)kvp.Value; break;
                    case "/references": element.References = (List<Reference>)kvp.Value; break;
                    case "/tags": element.Tags = (List<string>)kvp.Value; break;
                    case "/deleteStatus": element.DeleteStatus = (DeleteStatus)kvp.Value; break;
                    case "/lastChanged": element.LastChanged = (DateTime?)kvp.Value; break;
                    case "/lastChangedBy": element.LastChangedBy = (string)kvp.Value; break;
                    case "/fileScanResult": element.FileScanResult = (FileScanResult)kvp.Value; break;
                    case "/contentType": element.ContentType = (string)kvp.Value; break;
                    case "/filename": element.Filename = (string)kvp.Value; break;
                    case "/size": element.Size = (long)kvp.Value; break;
                    default: throw new ArgumentException("Unexpected key " + kvp.Key);
                }
            }

            await using NpgsqlCommand pgcom = new(_updateSql, transConnection)
            {
                Parameters =
                {
                    new() { Value = dataElementId, NpgsqlDbType = NpgsqlDbType.Uuid },
                    new() { Value = element, NpgsqlDbType = NpgsqlDbType.Jsonb },
                },
            };
            await pgcom.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return element;
        }
     }
}
