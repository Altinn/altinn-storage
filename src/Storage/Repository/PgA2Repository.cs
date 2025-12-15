using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Microsoft.Extensions.Options;
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
/// <param name="outboxRepository">The outbox repository.</param>
/// <param name="wolverineSettings">Wolverine settings</param>
public class PgA2Repository(
    NpgsqlDataSource dataSource,
    IOutboxRepository outboxRepository,
    IOptions<WolverineSettings> wolverineSettings
) : IA2Repository
{
    private static readonly string _readXslSql =
        "select * from storage.reada2xsls (@_org, @_app, @_lformid, @_language, @_xsltype)";
    private static readonly string _insertXslSql =
        "call storage.inserta2xsl (@_org, @_app, @_lformid, @_language, @_pagenumber, @_xsl, @_xsltype, @_isportrait)";
    private static readonly string _insertCodelistSql =
        "call storage.inserta2codelist (@_name, @_language, @_version, @_codelist)";
    private static readonly string _insertImageSql = "call storage.inserta2image (@_name, @_image)";
    private static readonly string _readCodelistSql =
        "select * from storage.reada2codelist (@_name, @_language)";
    private static readonly string _readImageSql = "select * from storage.reada2image (@_name)";

    private static readonly string _readA1MigrationStateSql =
        "select * from storage.reada1migrationstate (@_a1archivereference)";
    private static readonly string _readA2MigrationStateSql =
        "select * from storage.reada2migrationstate (@_a2archivereference)";
    private static readonly string _insertA1MigrationStateSql =
        "call storage.inserta1migrationstate (@_a1archivereference)";
    private static readonly string _insertA2MigrationStateSql =
        "call storage.inserta2migrationstate (@_a2archivereference)";
    private static readonly string _updateA1MigrationStateStartedSql =
        "call storage.updatea1migrationstatestarted (@_a1archivereference, @_instanceguid)";
    private static readonly string _updateA2MigrationStateStartedSql =
        "call storage.updatea2migrationstatestarted (@_a2archivereference, @_instanceguid)";
    private static readonly string _updateMigrationStateCompletedSql =
        "call storage.updatemigrationstatecompleted (@_instanceguid)";
    private static readonly string _deleteMigrationStateSql =
        "call storage.deletemigrationstate (@_instanceguid)";

    private readonly NpgsqlDataSource _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task CreateXsl(
        string org,
        string app,
        int lformId,
        string language,
        int pageNumber,
        string xsl,
        int xslType,
        bool isPortrait
    )
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertXslSql);
        pgcom.Parameters.AddWithValue("_org", NpgsqlDbType.Text, org);
        pgcom.Parameters.AddWithValue("_app", NpgsqlDbType.Text, app);
        pgcom.Parameters.AddWithValue("_lformid", NpgsqlDbType.Integer, lformId);
        pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
        pgcom.Parameters.AddWithValue("_pagenumber", NpgsqlDbType.Integer, pageNumber);
        pgcom.Parameters.AddWithValue("_xsl", NpgsqlDbType.Text, xsl);
        pgcom.Parameters.AddWithValue("_xsltype", NpgsqlDbType.Integer, xslType);
        pgcom.Parameters.AddWithValue("_isportrait", NpgsqlDbType.Boolean, isPortrait);

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task CreateCodelist(string name, string language, int version, string codelist)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertCodelistSql);
        pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);
        pgcom.Parameters.AddWithValue("_language", NpgsqlDbType.Text, language);
        pgcom.Parameters.AddWithValue("_version", NpgsqlDbType.Integer, version);
        pgcom.Parameters.AddWithValue("_codelist", NpgsqlDbType.Text, codelist);

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task CreateImage(string name, byte[] image)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertImageSql);
        pgcom.Parameters.AddWithValue("_name", NpgsqlDbType.Text, name);
        pgcom.Parameters.AddWithValue("_image", NpgsqlDbType.Bytea, image);

        await pgcom.ExecuteNonQueryAsync();
    }

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

    /// <inheritdoc/>
    public async Task CreateA1MigrationState(int a1ArchiveReference)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertA1MigrationStateSql);
        pgcom.Parameters.AddWithValue(
            "_a1archivereference",
            NpgsqlDbType.Bigint,
            a1ArchiveReference
        );

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task CreateA2MigrationState(int a2ArchiveReference)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_insertA2MigrationStateSql);
        pgcom.Parameters.AddWithValue(
            "_a2archivereference",
            NpgsqlDbType.Bigint,
            a2ArchiveReference
        );

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task UpdateStartA1MigrationState(int a1ArchiveReference, string instanceGuid)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
            _updateA1MigrationStateStartedSql
        );
        pgcom.Parameters.AddWithValue(
            "_a1archivereference",
            NpgsqlDbType.Bigint,
            a1ArchiveReference
        );
        pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task UpdateStartA2MigrationState(int a2ArchiveReference, string instanceGuid)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(
            _updateA2MigrationStateStartedSql
        );
        pgcom.Parameters.AddWithValue(
            "_a2archivereference",
            NpgsqlDbType.Bigint,
            a2ArchiveReference
        );
        pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task UpdateCompleteMigrationState(Instance instance)
    {
        string instanceId = instance.Id.Split('/')[^1];
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using NpgsqlCommand pgcom = new(_updateMigrationStateCompletedSql, connection);
        pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceId));

        await pgcom.ExecuteNonQueryAsync();

        if (wolverineSettings.Value.EnableSending && wolverineSettings.Value.EnableA2Migration)
        {
            SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                instance.AppId,
                instance.InstanceOwner.PartyId,
                instanceId,
                (DateTime)instance.Created,
                true,
                InstanceEventType.Created
            );
            await outboxRepository.Insert(instanceUpdateCommand, connection);
        }

        await tx.CommitAsync();
    }

    /// <inheritdoc/>
    public async Task SendDeleteToDialogporten(Instance instance)
    {
        if (!(wolverineSettings.Value.EnableSending && wolverineSettings.Value.EnableA2Migration))
        {
            return;
        }

        string instanceId = instance.Id.Split('/')[^1];
        await using var connection = await _dataSource.OpenConnectionAsync();
        SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
            instance.AppId,
            instance.InstanceOwner.PartyId,
            instanceId,
            (DateTime)instance.Created,
            true,
            InstanceEventType.Deleted
        );

        await outboxRepository.Insert(instanceUpdateCommand, connection);
    }

    /// <inheritdoc/>
    public async Task DeleteMigrationState(string instanceGuid)
    {
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_deleteMigrationStateSql);
        pgcom.Parameters.AddWithValue("_instanceGuid", NpgsqlDbType.Uuid, new Guid(instanceGuid));

        await pgcom.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task<string> GetA1MigrationInstanceId(int a1ArchiveReference)
    {
        string instanceId = null;
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readA1MigrationStateSql);
        pgcom.Parameters.AddWithValue(
            "_a1archivereference",
            NpgsqlDbType.Bigint,
            a1ArchiveReference
        );

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            instanceId = await reader.IsDBNullAsync("instanceguid")
                ? null
                : (await reader.GetFieldValueAsync<Guid>("instanceguid")).ToString();
        }

        return instanceId;
    }

    /// <inheritdoc/>
    public async Task<string> GetA2MigrationInstanceId(int a2ArchiveReference)
    {
        string instanceId = null;
        await using NpgsqlCommand pgcom = _dataSource.CreateCommand(_readA2MigrationStateSql);
        pgcom.Parameters.AddWithValue(
            "_a2archivereference",
            NpgsqlDbType.Bigint,
            a2ArchiveReference
        );

        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            instanceId = await reader.IsDBNullAsync("instanceguid")
                ? null
                : (await reader.GetFieldValueAsync<Guid>("instanceguid")).ToString();
        }

        return instanceId;
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
