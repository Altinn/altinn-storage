#nullable disable

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.UnitTest.Configuration;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

[Collection("StoragePostgreSQL")]
public sealed class DomainModelNpgsqlContractTests : IClassFixture<DomainModelNpgsqlFixture>
{
    private readonly NpgsqlDataSource _dataSource;

    public DomainModelNpgsqlContractTests(DomainModelNpgsqlFixture fixture)
    {
        _dataSource = fixture.DataSource;
    }

    [Fact]
    public async Task DynamicJson_InstanceDomainAndApi_MatchIndependentDatabaseGolden()
    {
        string domainJson = await SerializeThroughNpgsql(
            DomainModelContractTestData.CreateDomainInstance()
        );
        JsonObject apiJson = JsonNode
            .Parse(
                await SerializeThroughNpgsql(
                    DomainModelContractTestData.CreateApiInstance(apiFormatId: false)
                )
            )
            .AsObject();

        Assert.True(apiJson.Remove("Data"));
        Assert.True(apiJson.Remove("SelfLinks"));
        DomainModelContractTests.AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedInstanceDatabaseJson,
            domainJson
        );
        DomainModelContractTests.AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedInstanceDatabaseJson,
            apiJson.ToJsonString()
        );
    }

    [Fact]
    public async Task DynamicJson_DataElementDomainAndApi_MatchIndependentDatabaseGolden()
    {
        string domainJson = await SerializeThroughNpgsql(
            DomainModelContractTestData.CreateDomainDataElement()
        );
        JsonObject apiJson = JsonNode
            .Parse(await SerializeThroughNpgsql(DomainModelContractTestData.CreateApiDataElement()))
            .AsObject();

        Assert.True(apiJson.Remove("SelfLinks"));
        DomainModelContractTests.AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedDataElementDatabaseJson,
            domainJson
        );
        DomainModelContractTests.AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedDataElementDatabaseJson,
            apiJson.ToJsonString()
        );
    }

    [Fact]
    public async Task DynamicJson_ReadsLegacyInstanceJson_WithIgnoredNullOrAbsentKeys()
    {
        InstanceInternal withNullKeys = await DeserializeThroughNpgsql<InstanceInternal>(
            DomainModelContractTestData.LegacyInstanceJsonWithIgnoredNullKeys
        );
        InstanceInternal withoutKeys = await DeserializeThroughNpgsql<InstanceInternal>(
            DomainModelContractTestData.LegacyInstanceJsonWithoutIgnoredKeys
        );

        Assert.Equal("ABCDEF12-3456-4789-ABCD-EF1234567890", withNullKeys.Id);
        Assert.Equal("legacy-org", withNullKeys.Org);
        Assert.Null(withNullKeys.AppId);
        Assert.Null(withNullKeys.DueBefore);
        Assert.Equal("Task_Legacy", withNullKeys.Process.CurrentTask.ElementId);
        Assert.Equal(ReadStatus.UpdatedSinceLastReview, withNullKeys.Status.ReadStatus);
        Assert.Equal("legacy.label", withNullKeys.Status.Substatus.Label);
        Assert.Null(withNullKeys.CompleteConfirmations);
        Assert.Null(withNullKeys.PresentationTexts);
        Assert.Equal("yes", withNullKeys.DataValues["legacy"]);
        Assert.Null(withNullKeys.Data);
        Assert.Null(withNullKeys.Versions);
        Assert.Equal(0, withNullKeys.InternalId);

        Assert.Equal("mixedCase-Id-Is-Preserved", withoutKeys.Id);
        Assert.Equal("legacy/app", withoutKeys.AppId);
        Assert.Null(withoutKeys.InstanceOwner);
        Assert.Null(withoutKeys.Status);
        Assert.Null(withoutKeys.DataValues);
        Assert.Null(withoutKeys.Data);
        Assert.Null(withoutKeys.Versions);
    }

    [Fact]
    public async Task DynamicJson_ReadsLegacyDataElementJson_WithIgnoredNullOrAbsentKeyAndDefaults()
    {
        DataElementInternal withNullKey = await DeserializeThroughNpgsql<DataElementInternal>(
            DomainModelContractTestData.LegacyDataElementJsonWithIgnoredNullKey
        );
        DataElementInternal withoutKey = await DeserializeThroughNpgsql<DataElementInternal>(
            DomainModelContractTestData.LegacyDataElementJsonWithoutIgnoredKey
        );

        Assert.Equal("legacy-non-guid-data-element-id", withNullKey.Id);
        Assert.Equal("legacy-non-guid-instance-id", withNullKey.InstanceGuid);
        Assert.Null(withNullKey.Filename);
        Assert.Null(withNullKey.BlobStoragePath);
        Assert.True(withNullKey.IsRead);
        Assert.Empty(withNullKey.Tags);
        Assert.Null(withNullKey.Refs);
        Assert.Null(withNullKey.UserDefinedMetadata);
        Assert.Null(withNullKey.Metadata[0].Value);
        Assert.True(withNullKey.DeleteStatus.IsHardDeleted);
        Assert.Equal(FileScanResult.Infected, withNullKey.FileScanResult);
        Assert.Equal(RelationType.GeneratedFrom, withNullKey.References[0].Relation);
        Assert.Equal(ReferenceType.Task, withNullKey.References[0].ValueType);
        Assert.Null(withNullKey.BlobVersionId);
        Assert.Equal(DomainModelContractTestData.DataElementGuid, withoutKey.Id);
        Assert.Equal(DomainModelContractTestData.InstanceGuid, withoutKey.InstanceGuid);
        Assert.Null(withoutKey.DataType);
        Assert.Null(withoutKey.Filename);
        Assert.True(withoutKey.IsRead);
        Assert.Empty(withoutKey.Tags);
    }

    [Fact]
    public async Task StringInstanceId_PreservesLegacyCasingInsteadOfChangingArchiveMatching()
    {
        const string legacyId = "ABCDEF12-3456-4789-ABCD-EF1234567890";
        InstanceInternal domain = new() { Id = legacyId };
        InstanceQueryParameters query = new() { ArchiveReference = "EF1234567890" };
        string normalizedArchiveReference = (string)
            query.GeneratePostgreSQLParameters()["_archiveReference"];

        await using NpgsqlCommand command = _dataSource.CreateCommand(
            "select ($1::jsonb ->> 'Id'), ($1::jsonb ->> 'Id') like '%' || $2"
        );
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, domain);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, normalizedArchiveReference);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(legacyId, reader.GetString(0));
        Assert.Equal("ef1234567890", normalizedArchiveReference);
        Assert.False(reader.GetBoolean(1));
    }

    private async Task<string> SerializeThroughNpgsql<T>(T value)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand("select $1::jsonb::text");
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, value);
        return (string)await command.ExecuteScalarAsync();
    }

    private async Task<T> DeserializeThroughNpgsql<T>(string json)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand("select $1::jsonb");
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return await reader.GetFieldValueAsync<T>(0);
    }
}

public sealed class DomainModelNpgsqlFixture : IAsyncLifetime
{
    public NpgsqlDataSource DataSource { get; private set; }

    public Task InitializeAsync()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddJsonFile(ServiceUtil.GetAppsettingsPath())
            .AddEnvironmentVariables()
            .Build();

        PostgreSqlSettings settings =
            config.GetSection("PostgreSQLSettings").Get<PostgreSqlSettings>()
            ?? throw new ArgumentNullException(
                nameof(config),
                "Required PostgreSQLSettings is missing from application configuration"
            );
        string connectionString = string.Format(settings.ConnectionString, settings.StorageDbPwd);
        DataSource = new NpgsqlDataSourceBuilder(connectionString).EnableDynamicJson().Build();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }
    }
}
