#nullable disable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

/// <summary>
/// Pins the shape of every document written to a jsonb column. The SQL functions address the keys
/// in these documents directly and every stored row has this shape, so a snapshot change here is a
/// change to the persisted contract and needs a matching migration. The models come from the
/// Altinn.Platform.Storage.Interface package, so bumping that package can also change a snapshot.
/// </summary>
public class PersistedJsonContractTests
{
    private static readonly DateTime _created = new(2026, 9, 25, 8, 15, 30, 123, DateTimeKind.Utc);
    private static readonly DateTime _changed = _created.AddHours(2);
    private static readonly Guid _instanceGuid = new("8a9d6f4e-0f1e-4c2b-9a7d-2f3e4d5c6b7a");
    private static readonly Guid _dataGuid = new("d1c2b3a4-5f6e-4d7c-8b9a-0e1f2a3b4c5d");
    private static readonly Guid _refGuid = new("0f1e2d3c-4b5a-4968-8776-655443322110");
    private static readonly Guid _eventGuid = new("e5f6a7b8-c9d0-4e1f-a2b3-c4d5e6f7a8b9");
    private static readonly Guid _systemUserGuid = new("11111111-2222-4333-8444-555555555555");

    [Fact]
    public async Task Instance_PersistedShape()
    {
        await VerifyPersistedShape(BuildInstance());
    }

    [Fact]
    public async Task InstanceInternal_PersistedShape()
    {
        await VerifyPersistedShape(BuildInstanceInternal());
    }

    [Fact]
    public async Task DataElement_PersistedShape()
    {
        await VerifyPersistedShape(BuildDataElement());
    }

    [Fact]
    public async Task DataElementInternal_PersistedShape()
    {
        await VerifyPersistedShape(BuildDataElementInternal());
    }

    [Fact]
    public async Task InstanceEvent_PersistedShape()
    {
        await VerifyPersistedShape(BuildInstanceEvent());
    }

    [Fact]
    public async Task Application_PersistedShape()
    {
        await VerifyPersistedShape(BuildApplication());
    }

    [Fact]
    public async Task TextResource_PersistedShape()
    {
        await VerifyPersistedShape(BuildTextResource());
    }

    /// <summary>
    /// The options are the framework defaults that were in effect before they were pinned, so
    /// existing rows read back unchanged.
    /// </summary>
    [Fact]
    public void Options_MatchFrameworkDefaults()
    {
        JsonSerializerOptions defaults = new();
        JsonSerializerOptions pinned = PersistedJson.Options;

        Assert.True(pinned.IsReadOnly);
        Assert.Equal(defaults.PropertyNamingPolicy, pinned.PropertyNamingPolicy);
        Assert.Equal(defaults.DictionaryKeyPolicy, pinned.DictionaryKeyPolicy);
        Assert.Equal(defaults.PropertyNameCaseInsensitive, pinned.PropertyNameCaseInsensitive);
        Assert.Equal(defaults.DefaultIgnoreCondition, pinned.DefaultIgnoreCondition);
        Assert.Equal(defaults.IgnoreReadOnlyProperties, pinned.IgnoreReadOnlyProperties);
        Assert.Equal(defaults.IncludeFields, pinned.IncludeFields);
        Assert.Equal(defaults.NumberHandling, pinned.NumberHandling);
        Assert.Equal(defaults.UnmappedMemberHandling, pinned.UnmappedMemberHandling);
        Assert.Equal(defaults.WriteIndented, pinned.WriteIndented);
        Assert.Equal(defaults.Encoder, pinned.Encoder);
        Assert.Empty(pinned.Converters);
    }

    /// <summary>
    /// Serializes with the persistence options, snapshots the result, and checks that reading the
    /// document back with the same options reproduces it, which is what the row round trip relies on.
    /// </summary>
    private static async Task VerifyPersistedShape<T>(T document)
    {
        string json = JsonSerializer.Serialize(document, PersistedJson.Options);

        T roundTripped = JsonSerializer.Deserialize<T>(json, PersistedJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(roundTripped, PersistedJson.Options));

        string indented = JsonNode
            .Parse(json)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        VerifySettings settings = new();
        settings.DontScrubDateTimes();
        settings.DontScrubGuids();
        await Verifier.Verify(indented, extension: "json", settings: settings);
    }

    private static Instance BuildInstance() =>
        new()
        {
            Id = $"1337/{_instanceGuid}",
            InstanceOwner = BuildInstanceOwner(),
            AppId = "ttd/contract-app",
            Org = "ttd",
            SelfLinks = new ResourceLinks
            {
                Apps = "https://ttd.apps.altinn.no/ttd/contract-app/instances/1337/x",
                Platform = "https://platform.altinn.no/storage/api/v1/instances/1337/x",
            },
            DueBefore = _changed.AddDays(30),
            VisibleAfter = _created,
            Process = BuildProcessState(),
            Status = BuildInstanceStatus(),
            CompleteConfirmations = [BuildCompleteConfirmation()],
            Data = [BuildDataElement()],
            PresentationTexts = new Dictionary<string, string> { ["title"] = "Contract" },
            DataValues = new Dictionary<string, string> { ["caseNumber"] = "42" },
            Created = _created,
            CreatedBy = "1337",
            LastChanged = _changed,
            LastChangedBy = "1338",
        };

    private static InstanceInternal BuildInstanceInternal() =>
        new()
        {
            Id = _instanceGuid,
            InstanceOwner = BuildInstanceOwner(),
            AppId = "ttd/contract-app",
            Org = "ttd",
            DueBefore = _changed.AddDays(30),
            VisibleAfter = _created,
            Process = BuildProcessState(),
            Status = BuildInstanceStatus(),
            CompleteConfirmations = [BuildCompleteConfirmation()],
            Data = [BuildDataElementInternal()],
            PresentationTexts = new Dictionary<string, string> { ["title"] = "Contract" },
            DataValues = new Dictionary<string, string> { ["caseNumber"] = "42" },
            Created = _created,
            CreatedBy = "1337",
            LastChanged = _changed,
            LastChangedBy = "1338",
            Versions = new StorageVersions(7, 3),
            InternalId = 99,
        };

    private static InstanceOwner BuildInstanceOwner() =>
        new()
        {
            PartyId = "1337",
            PersonNumber = "01010112345",
            OrganisationNumber = "991825827",
            Username = "user-1337",
            ExternalIdentifier = "ext:1337",
        };

    private static ProcessState BuildProcessState() =>
        new()
        {
            Status = ProcessStatus.Processing,
            Started = _created,
            StartEvent = "StartEvent_1",
            CurrentTask = BuildProcessElementInfo(),
            Ended = _changed,
            EndEvent = "EndEvent_1",
        };

    private static ProcessElementInfo BuildProcessElementInfo()
    {
#pragma warning disable CS0618 // Obsolete members are still persisted.
        return new ProcessElementInfo
        {
            Flow = 2,
            Started = _created,
            ElementId = "Task_1",
            Name = "Utfylling",
            AltinnTaskType = "data",
            Ended = _changed,
            Validated = new ValidationStatus { Timestamp = _changed, CanCompleteTask = true },
            FlowType = "CompleteCurrentMoveToNext",
        };
#pragma warning restore CS0618
    }

    private static InstanceStatus BuildInstanceStatus() =>
        new()
        {
            IsArchived = true,
            Archived = _changed,
            IsSoftDeleted = true,
            SoftDeleted = _changed,
            IsHardDeleted = true,
            HardDeleted = _changed,
            ReadStatus = ReadStatus.UpdatedSinceLastReview,
            Substatus = new Substatus { Label = "substatus.label", Description = "substatus.desc" },
        };

    private static CompleteConfirmation BuildCompleteConfirmation() =>
        new() { StakeholderId = "ttd", ConfirmedOn = _changed };

    private static DataElement BuildDataElement() =>
        new()
        {
            Id = _dataGuid.ToString(),
            InstanceGuid = _instanceGuid.ToString(),
            DataType = "model",
            Filename = "skjema.xml",
            ContentType = "application/xml",
            BlobStoragePath = $"ttd/contract-app/{_instanceGuid}/data/{_dataGuid}",
            SelfLinks = new ResourceLinks
            {
                Apps = "https://ttd.apps.altinn.no/ttd/contract-app/instances/1337/x/data/y",
                Platform = "https://platform.altinn.no/storage/api/v1/instances/1337/x/data/y",
            },
            Size = 2048,
            ContentHash = "sha256:abc",
            BlobVersionId = "2026-09-25T08:15:30.1230000Z",
            Locked = true,
            Refs = [_refGuid],
            IsRead = false,
            Tags = ["signed"],
            UserDefinedMetadata = [new KeyValueEntry { Key = "user", Value = "meta" }],
            Metadata = [new KeyValueEntry { Key = "system", Value = "meta" }],
            DeleteStatus = new DeleteStatus { IsHardDeleted = true, HardDeleted = _changed },
            FileScanResult = FileScanResult.Clean,
            References =
            [
                new Reference
                {
                    Value = _refGuid.ToString(),
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.DataElement,
                },
            ],
            Created = _created,
            CreatedBy = "1337",
            LastChanged = _changed,
            LastChangedBy = "1338",
        };

    private static DataElementInternal BuildDataElementInternal() =>
        new()
        {
            Id = _dataGuid,
            InstanceGuid = _instanceGuid,
            DataType = "model",
            Filename = "skjema.xml",
            ContentType = "application/xml",
            BlobStoragePath = $"ttd/contract-app/{_instanceGuid}/data/{_dataGuid}",
            Size = 2048,
            ContentHash = "sha256:abc",
            Locked = true,
            Refs = [_refGuid],
            IsRead = false,
            Tags = ["signed"],
            UserDefinedMetadata = [new KeyValueEntry { Key = "user", Value = "meta" }],
            Metadata = [new KeyValueEntry { Key = "system", Value = "meta" }],
            DeleteStatus = new DeleteStatus { IsHardDeleted = true, HardDeleted = _changed },
            FileScanResult = FileScanResult.Clean,
            References =
            [
                new Reference
                {
                    Value = _refGuid.ToString(),
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.DataElement,
                },
            ],
            Created = _created,
            CreatedBy = "1337",
            LastChanged = _changed,
            LastChangedBy = "1338",
            BlobVersionId = "2026-09-25T08:15:30.1230000Z",
        };

    private static InstanceEvent BuildInstanceEvent() =>
        new()
        {
            Id = _eventGuid,
            InstanceId = $"1337/{_instanceGuid}",
            DataId = _dataGuid.ToString(),
            Created = _created,
            EventType = InstanceEventType.Saved.ToString(),
            InstanceOwnerPartyId = "1337",
            User = BuildPlatformUser(),
            RelatedUser = BuildPlatformUser(),
            ProcessInfo = BuildProcessState(),
            AdditionalInfo = "additional",
        };

    private static PlatformUser BuildPlatformUser() =>
        new()
        {
            UserId = 20001,
            OrgId = "ttd",
            AuthenticationLevel = 3,
            EndUserSystemId = 7,
            NationalIdentityNumber = "01010112345",
            SystemUserId = _systemUserGuid,
            SystemUserOwnerOrgNo = "991825827",
            SystemUserName = "system-user",
        };

    private static Application BuildApplication()
    {
#pragma warning disable CS0618 // Obsolete members are still persisted.
        DataType dataType = new()
        {
            Id = "model",
            Description = new LanguageString { ["nb"] = "Skjema" },
            AllowedContentTypes = ["application/xml"],
            AllowedContributers = ["org:ttd"],
            AllowedContributors = ["org:ttd"],
            ActionRequiredToRead = "read",
            ActionRequiredToWrite = "write",
            AppLogic = new ApplicationLogic
            {
                AutoCreate = true,
                ClassRef = "Altinn.App.Models.Skjema",
                SchemaRef = "App/models/Skjema.xsd",
                AllowAnonymousOnStateless = true,
                AutoDeleteOnProcessEnd = true,
                DisallowUserCreate = true,
                DisallowUserDelete = true,
                ShadowFields = new ShadowFields { Prefix = "AltinnSF_", SaveToDataType = "clean" },
            },
            TaskId = "Task_1",
            MaxSize = 1024,
            MaxCount = 2,
            MinCount = 1,
            Grouping = "group",
            EnablePdfCreation = false,
            EnableFileScan = true,
            ValidationErrorOnPendingFileScan = true,
            EnabledFileAnalysers = ["mimeTypeAnalyser"],
            EnabledFileValidators = ["mimeTypeValidator"],
            AllowedKeysForUserDefinedMetadata = ["user"],
        };
#pragma warning restore CS0618

        return new Application
        {
            Id = "ttd/contract-app",
            VersionId = "v1",
            Org = "ttd",
            Title = new Dictionary<string, string> { ["nb"] = "Kontrakt" },
            ValidFrom = _created,
            ValidTo = _changed.AddYears(1),
            ProcessId = "process",
            DataTypes = [dataType],
            PartyTypesAllowed = new PartyTypesAllowed
            {
                BankruptcyEstate = true,
                Organisation = true,
                Person = true,
                SubUnit = true,
            },
            AutoDeleteOnProcessEnd = true,
            PreventInstanceDeletionForDays = 30,
            PresentationFields =
            [
                new DataField
                {
                    Id = "title",
                    Path = "Skjema.Tittel",
                    DataTypeId = "model",
                },
            ],
            DataFields =
            [
                new DataField
                {
                    Id = "caseNumber",
                    Path = "Skjema.Sak",
                    DataTypeId = "model",
                },
            ],
            EFormidling = new EFormidlingContract
            {
                ServiceId = "DPF",
                DPFShipmentType = "altinn3.skjema",
                Receiver = "991825827",
                SendAfterTaskId = "Task_1",
                Process = "urn:no:difi:profile:arkivmelding:administrasjon:ver1.0",
                Standard = "urn:no:difi:arkivmelding:xsd::arkivmelding",
                TypeVersion = "2.0",
                Type = "arkivmelding",
                SecurityLevel = 3,
                DataTypes = ["model"],
            },
            OnEntry = new OnEntryConfig { Show = "select-instance" },
            MessageBoxConfig = new MessageBoxConfig
            {
                HideSettings = new HideSettings { HideAlways = true, HideOnTask = ["Task_1"] },
                SyncAdapterSettings = new SyncAdapterSettings
                {
                    DisableSync = true,
                    EnableUserSuppliedDialogId = true,
                    DisableCreate = true,
                    DisableDelete = true,
                    DisableAddActivities = true,
                    DisableAddTransmissions = true,
                    DisableSyncDueAt = true,
                    DisableSyncStatus = true,
                    DisableMarkCompletedWhenConfirmed = true,
                    DisableSyncContentTitle = true,
                    DisableSyncContentSummary = true,
                    DisableSyncContentAdditionalInformation = true,
                    DisableSyncContentExtendedStatus = true,
                    DisableSyncAttachments = true,
                    DisableSyncApiActions = true,
                    DisableSyncGuiActions = true,
                },
            },
            CopyInstanceSettings = new CopyInstanceSettings
            {
                Enabled = true,
                ExcludedDataTypes = ["attachment"],
                ExcludedDataFields = ["Skjema.Sak"],
                IncludeAttachments = true,
            },
            ApiScopes = new ApiScopesConfiguration
            {
                Users = new ApiScopes
                {
                    Read = "altinn:instances.read",
                    Write = "altinn:instances.write",
                    ErrorMessageTextResourceKey = "scopes.users",
                },
                ServiceOwners = new ApiScopes
                {
                    Read = "altinn:serviceowner/instances.read",
                    Write = "altinn:serviceowner/instances.write",
                    ErrorMessageTextResourceKey = "scopes.serviceowners",
                },
                ErrorMessageTextResourceKey = "scopes",
            },
            StorageAccountNumber = 2,
            DisallowUserInstantiation = true,
            Homepage = "https://ttd.apps.altinn.no/ttd/contract-app",
            Keywords = [new Keyword { Word = "kontrakt", Language = "nb" }],
            Description = new Dictionary<string, string> { ["nb"] = "Beskrivelse" },
            Access = new AppMetadataAccess
            {
                RightDescription = new Dictionary<string, string> { ["nb"] = "Rettighet" },
                Delegable = true,
                Visible = true,
            },
            ContactPoints =
            [
                new AppMetadataContactPoint
                {
                    Category = "support",
                    Email = "support@ttd.no",
                    Telephone = "+4700000000",
                    ContactPage = "https://ttd.no/kontakt",
                },
            ],
            Created = _created,
            CreatedBy = "ttd",
            LastChanged = _changed,
            LastChangedBy = "ttd",
        };
    }

    private static TextResource BuildTextResource() =>
        new()
        {
            Id = "ttd-contract-app-nb",
            Org = "ttd",
            Language = "nb",
            Resources =
            [
                new TextResourceElement
                {
                    Id = "appName",
                    Value = "Kontrakt {0}",
                    Variables =
                    [
                        new TextResourceVariable
                        {
                            Key = "Skjema.Tittel",
                            DataSource = "dataModel.model",
                            DefaultValue = "uten tittel",
                        },
                    ],
                },
            ],
        };
}
