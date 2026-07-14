#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class DomainModelContractTests
{
    // Literal tokens audited from PgInstanceRepository and every controller caller that builds
    // updateProperties. These are deliberately not nameof expressions: persisted key spelling is
    // the contract under test.
    public static TheoryData<string[], string> InstanceProductionUpdateCases()
    {
        return new()
        {
            { new[] { "Process" }, """{"Process":{}}""" },
            {
                new[] { "Status", "IsArchived", "Archived" },
                """{"Status":{"IsArchived":true,"Archived":"2024-02-03T04:05:06Z"}}"""
            },
            {
                new[] { "Status", "IsSoftDeleted", "SoftDeleted" },
                """{"Status":{"IsSoftDeleted":true,"SoftDeleted":"2024-02-04T05:06:07Z"}}"""
            },
            {
                new[] { "Status", "IsHardDeleted", "HardDeleted" },
                """{"Status":{"IsHardDeleted":true,"HardDeleted":"2024-02-05T06:07:08Z"}}"""
            },
            { new[] { "Status", "Substatus" }, """{"Status":{"Substatus":{}}}""" },
            {
                new[] { "Substatus", "LastChanged", "LastChangedBy" },
                """{"LastChanged":"2024-01-03T04:05:06Z","LastChangedBy":"last-changed-by"}"""
            },
            {
                new[] { "DataValues" },
                """{"DataValues":{"archiveReference":"bdb2a09da7ea","key":"value"}}"""
            },
            { new[] { "CompleteConfirmations" }, """{"CompleteConfirmations":[{}]}""" },
            {
                new[] { "PresentationTexts" },
                """{"PresentationTexts":{"nb":"Kontrakt","en":"Contract"}}"""
            },
            {
                new[] { "LastChanged", "LastChangedBy" },
                """{"LastChanged":"2024-01-03T04:05:06Z","LastChangedBy":"last-changed-by"}"""
            },
            { new[] { "Created" }, """{"Created":"2024-01-02T03:04:05Z"}""" },
            { new[] { "DueBefore" }, """{"DueBefore":"2024-04-05T06:07:08Z"}""" },
            { new[] { "VisibleAfter" }, """{"VisibleAfter":"2024-04-01T02:03:04Z"}""" },
        };
    }

    // Literal tokens audited from both data-element update switches. Nested token behavior is
    // intentionally pinned, including empty Reference/DeleteStatus objects when their child
    // tokens are not part of that production whitelist.
    public static TheoryData<string[], string> DataElementProductionUpdateCases()
    {
        return new()
        {
            { new[] { "Locked" }, """{"Locked":true}""" },
            { new[] { "Refs" }, """{"Refs":["99999999-8888-4777-8666-555555555555"]}""" },
            { new[] { "References" }, """{"References":[{}]}""" },
            { new[] { "Tags" }, """{"Tags":["contract","golden"]}""" },
            {
                new[] { "UserDefinedMetadata", "Key", "Value" },
                """{"UserDefinedMetadata":[{"Key":"user-key","Value":"user-value"}]}"""
            },
            {
                new[] { "Metadata", "Key", "Value" },
                """{"Metadata":[{"Key":"app-key","Value":"app-value"}]}"""
            },
            { new[] { "DeleteStatus" }, """{"DeleteStatus":{}}""" },
            { new[] { "LastChanged" }, """{"LastChanged":"2024-01-03T04:05:06Z"}""" },
            { new[] { "LastChangedBy" }, """{"LastChangedBy":"last-changed-by"}""" },
            { new[] { "FileScanResult" }, """{"FileScanResult":"Clean"}""" },
            { new[] { "ContentType" }, """{"ContentType":"application/pdf"}""" },
            { new[] { "Filename" }, """{"Filename":"contract.pdf"}""" },
            { new[] { "Size" }, """{"Size":12345}""" },
            {
                new[] { "BlobStoragePath" },
                """{"BlobStoragePath":"org/app/instance/data/element"}"""
            },
            { new[] { "IsRead" }, """{"IsRead":false}""" },
        };
    }

    [Fact]
    public void InstanceInternal_PublicProperties_MatchInstanceContract()
    {
        Dictionary<string, PropertyInfo> apiProperties = PublicProperties<Instance>();
        Dictionary<string, PropertyInfo> domainProperties = PublicProperties<InstanceInternal>();

        AssertPropertyNames(
            apiProperties,
            domainProperties,
            apiOnly: ["SelfLinks"],
            domainOnly: ["Versions", "InternalId"]
        );
        AssertMatchingTypes(
            apiProperties,
            domainProperties,
            new Dictionary<string, Type> { ["Data"] = typeof(List<DataElementInternal>) }
        );
        AssertJsonIgnoredProperties<InstanceInternal>("Data", "Versions", "InternalId");
        Assert.All(domainProperties.Values, property => Assert.True(property.CanWrite));
        Assert.False(typeof(Instance).IsAssignableFrom(typeof(InstanceInternal)));
        Assert.Null(
            typeof(InstanceInternal).GetMethod(
                "<Clone>$",
                BindingFlags.Instance | BindingFlags.Public
            )
        );
    }

    [Fact]
    public void DataElementInternal_PublicProperties_MatchDataElementContract()
    {
        Dictionary<string, PropertyInfo> apiProperties = PublicProperties<DataElement>();
        Dictionary<string, PropertyInfo> domainProperties = PublicProperties<DataElementInternal>();

        AssertPropertyNames(
            apiProperties,
            domainProperties,
            apiOnly: ["SelfLinks", "ContentEtag"],
            domainOnly: ["BlobVersionId"]
        );
        AssertMatchingTypes(apiProperties, domainProperties, new Dictionary<string, Type>());
        AssertJsonIgnoredProperties<DataElementInternal>("BlobVersionId");
        Assert.All(domainProperties.Values, property => Assert.True(property.CanWrite));
        Assert.False(typeof(DataElement).IsAssignableFrom(typeof(DataElementInternal)));
        Assert.Null(
            typeof(DataElementInternal).GetMethod(
                "<Clone>$",
                BindingFlags.Instance | BindingFlags.Public
            )
        );
    }

    [Fact]
    public void DataElementInternal_Defaults_MatchDataElementDefaults()
    {
        DataElement apiModel = new();
        DataElementInternal domainModel = new();

        Assert.Equal(apiModel.IsRead, domainModel.IsRead);
        Assert.Equal(apiModel.Tags, domainModel.Tags);
    }

    [Fact]
    public void DataElementInternal_HasNoApiModelCompatibilityBridge()
    {
        Assert.Null(
            typeof(DataElementInternal).GetProperty(
                "DataElement",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        );
        Assert.DoesNotContain(
            typeof(DataElementInternal).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ),
            constructor =>
                constructor
                    .GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(DataElement))
        );
    }

    [Fact]
    public void InstanceInternal_HasNoApiModelCompatibilityBridge()
    {
        Assert.Null(
            typeof(InstanceInternal).GetProperty(
                "Instance",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        );
        Assert.Null(
            typeof(InstanceInternal).GetProperty(
                "DataElements",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        );
        Assert.DoesNotContain(
            typeof(InstanceInternal).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ),
            constructor =>
                constructor
                    .GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(Instance))
        );
    }

    [Fact]
    public void DataElementToApiModel_MapsCompleteIndependentApiValueAndKeepsStorageStateOut()
    {
        DataElementInternal domain = DomainModelContractTestData.CreateDomainDataElement();
        DataElement expected = DomainModelContractTestData.CreateApiDataElement();
        expected.SelfLinks = null;

        DataElement actual = domain.ToApiModel();
        string actualJson = JsonConvert.SerializeObject(actual);

        AssertNewtonsoftJsonEqual(JsonConvert.SerializeObject(expected), actualJson);
        Assert.Null(actual.SelfLinks);
        Assert.DoesNotContain("blobVersionId", actualJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("api-content-version", domain.BlobVersionId);
        Assert.Same(domain.Refs, actual.Refs);
        Assert.Same(domain.Tags, actual.Tags);
        Assert.Same(domain.UserDefinedMetadata, actual.UserDefinedMetadata);
        Assert.Same(domain.Metadata, actual.Metadata);
        Assert.Same(domain.DeleteStatus, actual.DeleteStatus);
        Assert.Same(domain.References, actual.References);

        actual.Filename = "api-only.txt";
        Assert.Equal("contract.pdf", domain.Filename);
        actual.Metadata[0].Value = "shared-change";
        Assert.Equal("shared-change", domain.Metadata[0].Value);
    }

    [Fact]
    public void DataElementFromApiModel_MapsCompleteIndependentDatabaseValueAndIgnoresApiOnlyState()
    {
        DataElement api = DomainModelContractTestData.CreateApiDataElement();

        DataElementInternal actual = api.FromApiModel("mapped-blob-version");
        string actualJson = System.Text.Json.JsonSerializer.Serialize(actual);

        AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedDataElementDatabaseJson,
            actualJson
        );
        Assert.Equal("mapped-blob-version", actual.BlobVersionId);
        Assert.Equal("\"api-content-version\"", api.ContentEtag);
        Assert.DoesNotContain(nameof(DataElementInternal.BlobVersionId), actualJson);
        Assert.DoesNotContain("SelfLinks", PublicProperties<DataElementInternal>().Keys);
        Assert.Same(api.Refs, actual.Refs);
        Assert.Same(api.Tags, actual.Tags);
        Assert.Same(api.UserDefinedMetadata, actual.UserDefinedMetadata);
        Assert.Same(api.Metadata, actual.Metadata);
        Assert.Same(api.DeleteStatus, actual.DeleteStatus);
        Assert.Same(api.References, actual.References);

        api.Filename = "api-only.txt";
        Assert.Equal("contract.pdf", actual.Filename);
        api.Metadata[0].Value = "shared-change";
        Assert.Equal("shared-change", actual.Metadata[0].Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DataElementToApiModel_WithoutBlobVersion_OmitsContentEtag(string blobVersionId)
    {
        DataElementInternal domain = DomainModelContractTestData.CreateDomainDataElement();
        domain.BlobVersionId = blobVersionId;

        DataElement actual = domain.ToApiModel();
        string json = JsonConvert.SerializeObject(actual);

        Assert.Null(actual.ContentEtag);
        Assert.DoesNotContain("contentEtag", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DataRepository_DataElementSignaturesUseDomainValues()
    {
        MethodInfo[] methods = typeof(IDataRepository).GetMethods();

        Assert.All(
            methods.Where(method => method.Name == nameof(IDataRepository.Create)),
            method =>
            {
                Assert.Equal(typeof(DataElementInternal), method.GetParameters()[0].ParameterType);
                Assert.Equal(
                    typeof(DataElementWriteResult),
                    method.ReturnType.GetGenericArguments().Single()
                );
            }
        );
        Assert.Equal(
            typeof(DataElementInternal),
            Assert
                .Single(methods, method => method.Name == nameof(IDataRepository.Read))
                .ReturnType.GetGenericArguments()
                .Single()
        );
        Assert.All(
            methods.Where(method => method.Name == nameof(IDataRepository.Delete)),
            method =>
                Assert.Equal(typeof(DataElementInternal), method.GetParameters()[0].ParameterType)
        );
        Assert.Equal(
            typeof(DataElementInternal),
            Assert
                .Single(methods, method => method.Name == nameof(IDataRepository.DeleteForCleanup))
                .GetParameters()[0]
                .ParameterType
        );
        Assert.All(
            methods.Where(method => method.Name.StartsWith("Update", StringComparison.Ordinal)),
            method =>
                Assert.Equal(
                    typeof(DataElementWriteResult),
                    method.ReturnType.GetGenericArguments().Single()
                )
        );
    }

    [Fact]
    public void DataElementWriteResult_IsNonGenericAndCarriesDomainValue()
    {
        Assert.False(typeof(DataElementWriteResult).IsGenericType);
        Assert.Equal(
            typeof(DataElementInternal),
            typeof(DataElementWriteResult)
                .GetProperty(nameof(DataElementWriteResult.DataElement))!
                .PropertyType
        );
    }

    [Fact]
    public void InstanceRepository_WriteSignaturesUseDomainValuesAndMinimalIdentifier()
    {
        MethodInfo create = Assert.Single(
            typeof(IInstanceRepository).GetMethods(),
            method => method.Name == nameof(IInstanceRepository.Create)
        );
        Assert.Equal(typeof(InstanceInternal), create.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(InstanceInternal), create.ReturnType.GetGenericArguments().Single());

        MethodInfo delete = Assert.Single(
            typeof(IInstanceRepository).GetMethods(),
            method => method.Name == nameof(IInstanceRepository.Delete)
        );
        Assert.Equal(typeof(Guid), delete.GetParameters()[0].ParameterType);

        MethodInfo hardDeleted = Assert.Single(
            typeof(IInstanceRepository).GetMethods(),
            method => method.Name == nameof(IInstanceRepository.GetHardDeletedInstances)
        );
        Assert.Equal(
            typeof(List<InstanceInternal>),
            hardDeleted.ReturnType.GetGenericArguments().Single()
        );
    }

    [Fact]
    public void InstanceMutationCommit_UsesDomainAggregateValues()
    {
        Assert.Equal(
            typeof(InstanceInternal),
            typeof(InstanceMutationCommit)
                .GetProperty(nameof(InstanceMutationCommit.InstanceUpdates))!
                .PropertyType
        );
        Assert.Equal(
            typeof(IReadOnlyList<DataElementInternal>),
            typeof(InstanceMutationCommit)
                .GetProperty(nameof(InstanceMutationCommit.CreateDataElements))!
                .PropertyType
        );
        Assert.DoesNotContain(
            typeof(InstanceMutationCommit).GetProperties(),
            property => property.PropertyType == typeof(Instance)
        );
        Assert.DoesNotContain(
            typeof(InstanceMutationCommit).GetProperties(),
            property => property.PropertyType == typeof(DataElement)
        );
    }

    [Fact]
    public void InstanceToApiModel_MapsCompleteIndependentApiValueAndKeepsStorageStateOut()
    {
        InstanceInternal domain = DomainModelContractTestData.CreateDomainInstance();
        Instance expected = DomainModelContractTestData.CreateApiInstance(apiFormatId: true);
        expected.SelfLinks = null;
        expected.Data.ForEach(dataElement => dataElement.SelfLinks = null);

        Instance actual = domain.ToApiModel();
        string actualJson = JsonConvert.SerializeObject(actual);

        AssertNewtonsoftJsonEqual(JsonConvert.SerializeObject(expected), actualJson);
        Assert.Null(actual.SelfLinks);
        Assert.DoesNotContain("versions", actualJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalId", actualJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1337/045ea5db-6dd4-4476-b774-bdb2a09da7ea", actual.Id);
        Assert.Same(domain.InstanceOwner, actual.InstanceOwner);
        Assert.Same(domain.Process, actual.Process);
        Assert.Same(domain.Status, actual.Status);
        Assert.Same(domain.CompleteConfirmations, actual.CompleteConfirmations);
        Assert.Same(domain.PresentationTexts, actual.PresentationTexts);
        Assert.Same(domain.DataValues, actual.DataValues);
        Assert.NotSame(domain.Data, actual.Data);
        Assert.NotSame(domain.Data[0], actual.Data[0]);
        Assert.Same(domain.Data[0].Metadata, actual.Data[0].Metadata);

        actual.AppId = "api-only/app";
        Assert.Equal("org/contract-app", domain.AppId);
        actual.Status.IsArchived = false;
        Assert.False(domain.Status.IsArchived);
    }

    [Fact]
    public void InstanceFromApiModel_MapsCompleteIndependentDatabaseValueAndParsesId()
    {
        Instance api = DomainModelContractTestData.CreateApiInstance(apiFormatId: true);

        InstanceInternal actual = api.FromApiModel();
        string actualJson = System.Text.Json.JsonSerializer.Serialize(actual);

        AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedInstanceDatabaseJson,
            actualJson
        );
        Assert.Equal(DomainModelContractTestData.InstanceGuid, actual.Id);
        Assert.Null(actual.Versions);
        Assert.Equal(0, actual.InternalId);
        Assert.NotSame(api.Data, actual.Data);
        Assert.NotSame(api.Data[0], actual.Data[0]);
        Assert.Same(api.InstanceOwner, actual.InstanceOwner);
        Assert.Same(api.Process, actual.Process);
        Assert.Same(api.Status, actual.Status);
        Assert.Same(api.CompleteConfirmations, actual.CompleteConfirmations);
        Assert.Same(api.PresentationTexts, actual.PresentationTexts);
        Assert.Same(api.DataValues, actual.DataValues);
        Assert.Same(api.Data[0].Metadata, actual.Data[0].Metadata);
        Assert.DoesNotContain("SelfLinks", PublicProperties<InstanceInternal>().Keys);
    }

    [Theory]
    [InlineData("legacy-prefix/045ea5db-6dd4-4476-b774-bdb2a09da7ea")]
    [InlineData("42/045ea5db-6dd4-4476-b774-bdb2a09da7ea")]
    [InlineData("1337/045ea5db-6dd4-4476-b774-bdb2a09da7ea/legacy-suffix")]
    public void InstanceFromApiModel_PreservesLegacyCompositeIdTranslation(string id)
    {
        Instance api = DomainModelContractTestData.CreateApiInstance(apiFormatId: true);
        api.Id = id;

        InstanceInternal domain = api.FromApiModel();

        Assert.Equal(DomainModelContractTestData.InstanceGuid, domain.Id);
    }

    [Fact]
    public void InstanceMappings_PreserveNullsAndStorageIdCasing()
    {
        Instance api = new()
        {
            Id = "ABCDEF12-3456-4789-ABCD-EF1234567890",
            InstanceOwner = new InstanceOwner { PartyId = "42" },
        };

        InstanceInternal domain = api.FromApiModel();
        Instance roundTrip = domain.ToApiModel();

        Assert.Equal(api.Id, domain.Id);
        Assert.Equal($"42/{api.Id}", roundTrip.Id);
        Assert.Null(domain.Data);
        Assert.Null(roundTrip.Data);
        Assert.Null(roundTrip.SelfLinks);
    }

    [Theory]
    [InlineData(
        null,
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea"
    )]
    [InlineData("", "045ea5db-6dd4-4476-b774-bdb2a09da7ea", "045ea5db-6dd4-4476-b774-bdb2a09da7ea")]
    [InlineData(
        "   ",
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea"
    )]
    [InlineData("1337", "   ", "   ")]
    [InlineData(null, "   ", "   ")]
    [InlineData("   ", "\t", "\t")]
    [InlineData("1337", null, null)]
    [InlineData(null, null, null)]
    public void InstanceToApiModel_WithMissingIdOrOwner_PreservesRawOrNullId(
        string partyId,
        string instanceId,
        string expectedApiId
    )
    {
        InstanceInternal domain = DomainModelContractTestData.CreateDomainInstance();
        domain.Id = instanceId;
        domain.InstanceOwner = partyId is null ? null : new InstanceOwner { PartyId = partyId };

        // Persisted instances normally have both values. Partial service/legacy snapshots keep
        // their raw or null id rather than gaining a malformed synthetic wire id.
        Assert.Equal(expectedApiId, domain.ToApiModel().Id);
    }

    [Theory]
    [MemberData(nameof(InstanceProductionUpdateCases))]
    public void CustomSerializer_CoversEveryProductionInstanceToken(
        string[] literalTokens,
        string expectedJson
    )
    {
        InstanceInternal domain = DomainModelContractTestData.CreateDomainInstance();

        string actual = JsonHelper.CustomSerializer.Serialize(domain, [.. literalTokens]);

        AssertSystemTextJsonEqual(expectedJson, actual);
    }

    [Theory]
    [MemberData(nameof(DataElementProductionUpdateCases))]
    public void CustomSerializer_CoversEveryProductionDataElementToken(
        string[] literalTokens,
        string expectedJson
    )
    {
        DataElementInternal domain = DomainModelContractTestData.CreateDomainDataElement();

        string actual = JsonHelper.CustomSerializer.Serialize(domain, [.. literalTokens]);

        AssertSystemTextJsonEqual(expectedJson, actual);
    }

    [Fact]
    public void CustomSerializer_AllLiteralInstanceTokens_ProduceIndependentDatabaseGolden()
    {
        string[] allSerializableTokens =
        [
            "Id",
            "InstanceOwner",
            "PartyId",
            "PersonNumber",
            "OrganisationNumber",
            "Username",
            "ExternalIdentifier",
            "AppId",
            "Org",
            "DueBefore",
            "VisibleAfter",
            "Process",
            "Started",
            "StartEvent",
            "CurrentTask",
            "Flow",
            "ElementId",
            "Name",
            "AltinnTaskType",
            "Ended",
            "Validated",
            "Timestamp",
            "CanCompleteTask",
            "FlowType",
            "EndEvent",
            "Status",
            "IsArchived",
            "Archived",
            "IsSoftDeleted",
            "SoftDeleted",
            "IsHardDeleted",
            "HardDeleted",
            "ReadStatus",
            "Substatus",
            "Label",
            "Description",
            "CompleteConfirmations",
            "StakeholderId",
            "ConfirmedOn",
            "PresentationTexts",
            "DataValues",
            "Created",
            "CreatedBy",
            "LastChanged",
            "LastChangedBy",
        ];

        string actual = JsonHelper.CustomSerializer.Serialize(
            DomainModelContractTestData.CreateDomainInstance(),
            [.. allSerializableTokens]
        );

        AssertSystemTextJsonEqual(DomainModelContractTestData.ExpectedInstanceDatabaseJson, actual);
    }

    [Fact]
    public void CustomSerializer_AllLiteralDataElementTokens_ProduceIndependentDatabaseGolden()
    {
        string[] allSerializableTokens =
        [
            "Id",
            "InstanceGuid",
            "DataType",
            "Filename",
            "ContentType",
            "BlobStoragePath",
            "Size",
            "ContentHash",
            "Locked",
            "Refs",
            "IsRead",
            "Tags",
            "UserDefinedMetadata",
            "Metadata",
            "Key",
            "Value",
            "DeleteStatus",
            "IsHardDeleted",
            "HardDeleted",
            "FileScanResult",
            "References",
            "Relation",
            "ValueType",
            "Created",
            "CreatedBy",
            "LastChanged",
            "LastChangedBy",
        ];

        string actual = JsonHelper.CustomSerializer.Serialize(
            DomainModelContractTestData.CreateDomainDataElement(),
            [.. allSerializableTokens]
        );

        AssertSystemTextJsonEqual(
            DomainModelContractTestData.ExpectedDataElementDatabaseJson,
            actual
        );
    }

    [Fact]
    public void NewtonsoftApiWireShape_RemainsGoldenAndDomainTypesAreSeparate()
    {
        DataElement dataElement = DomainModelContractTestData.CreateApiDataElement();
        Instance instance = DomainModelContractTestData.CreateApiInstance(apiFormatId: true);

        AssertNewtonsoftJsonEqual(
            DomainModelContractTestData.ExpectedDataElementApiJson,
            JsonConvert.SerializeObject(dataElement)
        );
        AssertNewtonsoftJsonEqual(
            DomainModelContractTestData.ExpectedInstanceApiJson,
            JsonConvert.SerializeObject(instance)
        );

        Assert.False(
            typeof(Instance).IsInstanceOfType(DomainModelContractTestData.CreateDomainInstance())
        );
        Assert.False(
            typeof(DataElement).IsInstanceOfType(
                DomainModelContractTestData.CreateDomainDataElement()
            )
        );
    }

    private static Dictionary<string, PropertyInfo> PublicProperties<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(property => property.Name);

    private static void AssertPropertyNames(
        Dictionary<string, PropertyInfo> apiProperties,
        Dictionary<string, PropertyInfo> domainProperties,
        string[] apiOnly,
        string[] domainOnly
    )
    {
        string[] expectedDomainProperties = [.. apiProperties.Keys.Except(apiOnly), .. domainOnly];

        Assert.Equal(
            expectedDomainProperties.OrderBy(name => name),
            domainProperties.Keys.OrderBy(name => name)
        );
    }

    private static void AssertMatchingTypes(
        Dictionary<string, PropertyInfo> apiProperties,
        Dictionary<string, PropertyInfo> domainProperties,
        Dictionary<string, Type> documentedDomainTypes
    )
    {
        foreach ((string name, PropertyInfo domainProperty) in domainProperties)
        {
            if (!apiProperties.TryGetValue(name, out PropertyInfo apiProperty))
            {
                continue;
            }

            Assert.Equal(
                documentedDomainTypes.GetValueOrDefault(name, apiProperty.PropertyType),
                domainProperty.PropertyType
            );
        }
    }

    private static void AssertJsonIgnoredProperties<T>(params string[] expectedIgnored)
    {
        string[] actualIgnored = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()
                    is not null
            )
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(expectedIgnored.OrderBy(name => name), actualIgnored);
    }

    internal static void AssertSystemTextJsonEqual(string expected, string actual) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)),
            $"Expected: {expected}{Environment.NewLine}Actual: {actual}"
        );

    private static void AssertNewtonsoftJsonEqual(string expected, string actual)
    {
        Assert.Equal(
            Canonicalize(JToken.Parse(expected)).ToString(Formatting.None),
            Canonicalize(JToken.Parse(actual)).ToString(Formatting.None)
        );
    }

    private static JToken Canonicalize(JToken token) =>
        token switch
        {
            JObject value => new JObject(
                value
                    .Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new JProperty(property.Name, Canonicalize(property.Value)))
            ),
            JArray value => new JArray(value.Select(Canonicalize)),
            _ => token.DeepClone(),
        };
}
