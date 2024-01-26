using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.UnitTest.Utils;
using Npgsql;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class PostgresUtil
{
    public static async Task<int> RunCountQuery(string query)
    {
        NpgsqlDataSource dataSource = (NpgsqlDataSource)ServiceUtil.GetServices(new List<Type>() { typeof(NpgsqlDataSource) })[0]!;

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(query);
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<int>(0);
            }
        }

        throw new Exception("No results for " + query);
    }

    public static async Task<T> RunQuery<T>(string query)
    {
        NpgsqlDataSource dataSource = (NpgsqlDataSource)ServiceUtil.GetServices(new List<Type>() { typeof(NpgsqlDataSource) })[0]!;

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(query);
        await using (NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                return reader.GetFieldValue<T>(0);
            }
        }

        throw new Exception("No results for " + query);
    }

    public static async Task<int> RunSql(string query)
    {
        NpgsqlDataSource dataSource = (NpgsqlDataSource)ServiceUtil.GetServices(new List<Type>() { typeof(NpgsqlDataSource) })[0]!;

        await using NpgsqlCommand pgcom = dataSource.CreateCommand(query);
        return await pgcom.ExecuteNonQueryAsync();
    }
}
