#nullable disable

using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Handles a2 repository.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PgA2Repository"/> class.
/// </remarks>
/// <param name="dataSource">The npgsql data source.</param>
public class PgA2Repository(NpgsqlDataSource dataSource) : IA2Repository
{
    private static readonly string _readXslSql =
        "select * from storage.reada2xsls (@_org, @_app, @_lformid, @_language, @_xsltype)";
    private static readonly string _readCodelistSql =
        "select * from storage.reada2codelist (@_name, @_language)";
    private static readonly string _readImageSql = "select * from storage.reada2image (@_name)";

    private readonly NpgsqlDataSource _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<List<(string Xsl, bool IsPortrait)>> GetXsls(
        string org,
        string app,
        int lformId,
        string language,
        int xslType
    )
    {
        List<(string Xsl, bool IsPortraitl)> xsls = [];

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readXslSql);

        // Loop until language match
        foreach (string languageToTry in GetOrderedLanguages(language))
        {
            pgcom.Parameters.Clear();
            pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, org);
            pgcom.Parameters.AddWithValue("_app", NpgsqlDbType.Text, app);
            pgcom.Parameters.AddWithValue("_lformid", NpgsqlDbType.Integer, lformId);
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, languageToTry);
            pgcom.Parameters.AddWithValue("_xsltype", NpgsqlDbType.Integer, xslType);

            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                xsls.Add(
                    (
                        await reader.GetFieldValueAsync<string>("xsl"),
                        await reader.GetFieldValueAsync<bool>("isportrait")
                    )
                );
            }

            if (xsls.Count > 0)
            {
                return xsls;
            }
        }

        return xsls;
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetImage(string name)
    {
        byte[] image = null;

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readImageSql);
        pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            image = await reader.GetFieldValueAsync<byte[]>("image");
        }

        return image;
    }

    /// <inheritdoc/>
    public async Task<string> GetCodelist(string name, string preferredLanguage)
    {
        string codelist = null;
        string language = preferredLanguage;
        int retriesLeft = preferredLanguage != "nb" ? 2 : 1;

        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readCodelistSql);
        pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);

        while (codelist == null && retriesLeft > 0)
        {
            pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
            language = "nb";
            --retriesLeft;
            await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                codelist = await reader.GetFieldValueAsync<string>("codelist");
            }
        }

        return codelist ?? string.Empty;
    }

    private static List<string> GetOrderedLanguages(string language)
    {
        return language switch
        {
            "nb" => ["nb", "nn", "en"],
            "nn" => ["nn", "nb", "en"],
            "en" => ["en", "nb", "nn"],
            _ => ["nb", "nn", "en"],
        };
    }
}
