#nullable disable

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Reads the instance columns shared by the storage functions that return an instance with its
/// data elements, and the instance version counters every mutation function returns alongside
/// its own result columns.
/// </summary>
internal static class InstanceResultReader
{
    private const string _elementColumn = "element";

    internal static async Task<InstanceInternal> ReadAsync(
        NpgsqlDataReader reader,
        bool includeElements,
        CancellationToken cancellationToken,
        Action<NpgsqlDataReader> firstRowCallback = null
    )
    {
        InstanceInternal instance = null;
        List<DataElementInternal> instanceData = [];
        StorageVersions versions = null;
        long instanceInternalId = 0;
        bool instanceCreated = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!instanceCreated)
            {
                instanceCreated = true;
                firstRowCallback?.Invoke(reader);
                instance = await reader.GetFieldValueAsync<InstanceInternal>(
                    "instance",
                    cancellationToken
                );
                versions = ReadVersions(reader);
                instanceInternalId = await reader.GetFieldValueAsync<long>("id", cancellationToken);
            }

            if (includeElements && !await reader.IsDBNullAsync(_elementColumn, cancellationToken))
            {
                DataElementInternal element = await reader.GetFieldValueAsync<DataElementInternal>(
                    _elementColumn,
                    cancellationToken
                );
                int versionOrdinal = reader.GetOrdinal("currentblobversion");
                string blobVersionId = await reader.IsDBNullAsync(versionOrdinal, cancellationToken)
                    ? null
                    : BlobVersionId.Encode(reader.GetGuid(versionOrdinal));
                element.BlobVersionId = blobVersionId;
                instanceData.Add(element);
            }
        }

        if (instance is null)
        {
            return null;
        }

        instance.Data = instanceData.OrderBy(x => x.Created).ToList();
        instance.Versions = versions;
        instance.InternalId = instanceInternalId;
        return instance;
    }

    internal static StorageVersions ReadVersions(NpgsqlDataReader reader) =>
        new(
            reader.GetInt32(reader.GetOrdinal("instanceversion")),
            reader.GetInt32(reader.GetOrdinal("processstateversion"))
        );
}
