using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;
using static Altinn.Platform.Storage.Repository.JsonHelper;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IDataRepository"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="PgDataRepository"/> class.
    /// </remarks>
    /// <param name="logger">The logger to use when writing to logs.</param>
    /// <param name="dataSource">The npgsql data source.</param>
    /// <param name="telemetryClient">Telemetry client</param>
    public class PgDataRepository(
        ILogger<PgDataRepository> logger,
        NpgsqlDataSource dataSource,
        TelemetryClient telemetryClient = null) : IDataRepository
    {
        private readonly string _insertSql = "call storage.insertdataelement ($1, $2, $3, $4)";
        private readonly string _readSql = "select * from storage.readdataelement($1)";
        private readonly string _deleteSql = "select * from storage.deletedataelement_v2 ($1, $2, $3)";
        private readonly string _deleteForInstanceSql = "select * from storage.deletedataelements ($1)";
        private readonly string _updateSql = "select * from storage.updatedataelement_v2 ($1, $2, $3, $4, $5, $6)";

        private readonly ILogger<PgDataRepository> _logger = logger;
        private readonly NpgsqlDataSource _dataSource = dataSource;
        private readonly TelemetryClient _telemetryClient = telemetryClient;

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0)
        {
            dataElement.Id ??= Guid.NewGuid().ToString();
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, dataElement);

            await pgcom.ExecuteNonQueryAsync();

            tracker.Track();
            return dataElement;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.Id));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(dataElement.InstanceGuid));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, dataElement.LastChangedBy);

            int rc = (int)await pgcom.ExecuteScalarAsync();
            tracker.Track();
            return rc == 1;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteForInstance(string instanceId)
        {
            try
            {
                await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteForInstanceSql);
                using TelemetryTracker tracker = new(_telemetryClient, pgcom);
                pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, new Guid(instanceId));

                await pgcom.ExecuteScalarAsync();
                tracker.Track();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting data elements for instance {instanceId}", instanceId);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementId)
        {
            DataElement dataElement = null;
            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                dataElement = reader.GetFieldValue<DataElement>("element");
            }

            tracker.Track();
            return dataElement;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist)
        {
            if (propertylist.Count > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(propertylist), "PropertyList can contain at most 12 entries.");
            }

            List<string> elementProperties = [];
            List<string> instanceProperties = [];
            DataElement element = new();
            Instance lastChangedWrapper = new();
            bool isReadChangedToFalse = false;
            foreach (var kvp in propertylist)
            {
                switch (kvp.Key)
                {
                    case "/locked": element.Locked = (bool)kvp.Value; elementProperties.Add(nameof(DataElement.Locked)); break;
                    case "/refs": element.Refs = (List<Guid>)kvp.Value; elementProperties.Add(nameof(DataElement.Refs)); break;
                    case "/references": element.References = (List<Reference>)kvp.Value; elementProperties.Add(nameof(DataElement.References)); break;
                    case "/tags": element.Tags = (List<string>)kvp.Value; elementProperties.Add(nameof(DataElement.Tags)); break;
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

            await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_updateSql);
            using TelemetryTracker tracker = new(_telemetryClient, pgcom);

            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, dataElementId);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, CustomSerializer.Serialize(element, elementProperties));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Jsonb, CustomSerializer.Serialize(lastChangedWrapper, instanceProperties));
            pgcom.Parameters.AddWithValue(NpgsqlDbType.Boolean, isReadChangedToFalse);
            pgcom.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, lastChangedWrapper.LastChanged ?? (object)DBNull.Value);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                element = reader.GetFieldValue<DataElement>("updatedElement");
            }

            tracker.Track();
            return element;
        }
     }
}
