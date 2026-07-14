#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

internal static class DomainModelContractTestData
{
    internal const string InstanceGuid = "045ea5db-6dd4-4476-b774-bdb2a09da7ea";
    internal const string DataElementGuid = "11111111-2222-4333-8444-555555555555";

    internal const string ExpectedInstanceDatabaseJson = """
        {
          "Id": "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
          "InstanceOwner": {
            "PartyId": "1337",
            "PersonNumber": "01010112345",
            "OrganisationNumber": "999888777",
            "Username": "contract-user",
            "ExternalIdentifier": "external-user"
          },
          "AppId": "org/contract-app",
          "Org": "org",
          "DueBefore": "2024-04-05T06:07:08Z",
          "VisibleAfter": "2024-04-01T02:03:04Z",
          "Process": {
            "Started": "2024-02-01T01:02:03Z",
            "StartEvent": "StartEvent_1",
            "CurrentTask": {
              "Flow": 7,
              "Started": "2024-02-01T02:03:04Z",
              "ElementId": "Task_1",
              "Name": "Contract task",
              "AltinnTaskType": "data",
              "Ended": "2024-02-01T03:04:05Z",
              "Validated": {
                "Timestamp": "2024-02-01T02:30:00Z",
                "CanCompleteTask": true
              },
              "FlowType": "CompleteCurrentMoveToNext"
            },
            "Ended": "2024-02-02T03:04:05Z",
            "EndEvent": "EndEvent_1"
          },
          "Status": {
            "IsArchived": true,
            "Archived": "2024-02-03T04:05:06Z",
            "IsSoftDeleted": true,
            "SoftDeleted": "2024-02-04T05:06:07Z",
            "IsHardDeleted": true,
            "HardDeleted": "2024-02-05T06:07:08Z",
            "ReadStatus": 2,
            "Substatus": {
              "Label": "substatus.label",
              "Description": "substatus.description"
            }
          },
          "CompleteConfirmations": [{
            "StakeholderId": "org",
            "ConfirmedOn": "2024-02-06T07:08:09Z"
          }],
          "PresentationTexts": {"nb": "Kontrakt", "en": "Contract"},
          "DataValues": {"archiveReference": "bdb2a09da7ea", "key": "value"},
          "Created": "2024-01-02T03:04:05Z",
          "CreatedBy": "created-by",
          "LastChanged": "2024-01-03T04:05:06Z",
          "LastChangedBy": "last-changed-by"
        }
        """;

    internal const string ExpectedDataElementDatabaseJson = """
        {
          "Id": "11111111-2222-4333-8444-555555555555",
          "InstanceGuid": "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
          "DataType": "contract-data",
          "Filename": "contract.pdf",
          "ContentType": "application/pdf",
          "BlobStoragePath": "org/app/instance/data/element",
          "Size": 12345,
          "ContentHash": "YWJjZA==",
          "Locked": true,
          "Refs": ["99999999-8888-4777-8666-555555555555"],
          "IsRead": false,
          "Tags": ["contract", "golden"],
          "UserDefinedMetadata": [{"Key": "user-key", "Value": "user-value"}],
          "Metadata": [{"Key": "app-key", "Value": "app-value"}],
          "DeleteStatus": {
            "IsHardDeleted": true,
            "HardDeleted": "2024-03-04T05:06:07Z"
          },
          "FileScanResult": "Clean",
          "References": [{
            "Value": "Task_1",
            "Relation": "GeneratedFrom",
            "ValueType": "Task"
          }],
          "Created": "2024-01-02T03:04:05Z",
          "CreatedBy": "created-by",
          "LastChanged": "2024-01-03T04:05:06Z",
          "LastChangedBy": "last-changed-by"
        }
        """;

    internal const string LegacyInstanceJsonWithIgnoredNullKeys = """
        {
          "Id": "ABCDEF12-3456-4789-ABCD-EF1234567890",
          "InstanceOwner": {"PartyId": "42", "Username": null},
          "AppId": null,
          "Org": "legacy-org",
          "SelfLinks": null,
          "Data": null,
          "DueBefore": null,
          "VisibleAfter": "2020-01-02T03:04:05Z",
          "Process": {
            "Started": "2020-01-01T01:02:03Z",
            "StartEvent": "legacy-start",
            "CurrentTask": {"Flow": 3, "ElementId": "Task_Legacy"},
            "Ended": null,
            "EndEvent": null
          },
          "Status": {
            "IsArchived": false,
            "Archived": null,
            "IsSoftDeleted": true,
            "SoftDeleted": "2020-01-03T04:05:06Z",
            "IsHardDeleted": false,
            "HardDeleted": null,
            "ReadStatus": 2,
            "Substatus": {"Label": "legacy.label", "Description": null}
          },
          "CompleteConfirmations": null,
          "PresentationTexts": null,
          "DataValues": {"legacy": "yes"},
          "Created": "2020-01-01T00:00:00Z",
          "CreatedBy": null,
          "LastChanged": "2020-01-04T05:06:07Z",
          "LastChangedBy": "legacy-user"
        }
        """;

    internal const string LegacyInstanceJsonWithoutIgnoredKeys = """
        {
          "Id": "mixedCase-Id-Is-Preserved",
          "InstanceOwner": null,
          "AppId": "legacy/app",
          "Org": null,
          "Status": null,
          "DataValues": null
        }
        """;

    internal const string LegacyDataElementJsonWithIgnoredNullKey = """
        {
          "Id": "legacy-non-guid-data-element-id",
          "InstanceGuid": "legacy-non-guid-instance-id",
          "DataType": "legacy-data",
          "Filename": null,
          "ContentType": "text/plain",
          "BlobStoragePath": null,
          "SelfLinks": null,
          "Size": 7,
          "ContentHash": null,
          "Locked": false,
          "Refs": null,
          "UserDefinedMetadata": null,
          "Metadata": [{"Key": "legacy", "Value": null}],
          "DeleteStatus": {"IsHardDeleted": true, "HardDeleted": null},
          "FileScanResult": "Infected",
          "References": [{
            "Value": "legacy-task",
            "Relation": "GeneratedFrom",
            "ValueType": "Task"
          }],
          "Created": null,
          "CreatedBy": null,
          "LastChanged": "2020-02-03T04:05:06Z",
          "LastChangedBy": "legacy-user"
        }
        """;

    internal const string LegacyDataElementJsonWithoutIgnoredKey = """
        {
          "Id": "11111111-2222-4333-8444-555555555555",
          "InstanceGuid": "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
          "DataType": null,
          "Filename": null
        }
        """;

    internal const string ExpectedDataElementApiJson = """
        {
          "id": "11111111-2222-4333-8444-555555555555",
          "instanceGuid": "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
          "dataType": "contract-data",
          "filename": "contract.pdf",
          "contentType": "application/pdf",
          "blobStoragePath": "org/app/instance/data/element",
          "selfLinks": {
            "apps": "https://apps.example/data",
            "platform": "https://platform.example/data"
          },
          "size": 12345,
          "contentHash": "YWJjZA==",
          "contentEtag": "\"api-content-version\"",
          "locked": true,
          "refs": ["99999999-8888-4777-8666-555555555555"],
          "isRead": false,
          "tags": ["contract", "golden"],
          "userDefinedMetadata": [{"key": "user-key", "value": "user-value"}],
          "metadata": [{"key": "app-key", "value": "app-value"}],
          "deleteStatus": {
            "isHardDeleted": true,
            "hardDeleted": "2024-03-04T05:06:07Z"
          },
          "fileScanResult": "Clean",
          "references": [{
            "value": "Task_1",
            "relation": "GeneratedFrom",
            "valueType": "Task"
          }],
          "created": "2024-01-02T03:04:05Z",
          "createdBy": "created-by",
          "lastChanged": "2024-01-03T04:05:06Z",
          "lastChangedBy": "last-changed-by"
        }
        """;

    internal const string ExpectedInstanceApiJson = """
        {
          "id": "1337/045ea5db-6dd4-4476-b774-bdb2a09da7ea",
          "instanceOwner": {
            "partyId": "1337",
            "personNumber": "01010112345",
            "organisationNumber": "999888777",
            "username": "contract-user",
            "externalIdentifier": "external-user"
          },
          "appId": "org/contract-app",
          "org": "org",
          "selfLinks": {
            "apps": "https://apps.example/instance",
            "platform": "https://platform.example/instance"
          },
          "dueBefore": "2024-04-05T06:07:08Z",
          "visibleAfter": "2024-04-01T02:03:04Z",
          "process": {
            "started": "2024-02-01T01:02:03Z",
            "startEvent": "StartEvent_1",
            "currentTask": {
              "flow": 7,
              "started": "2024-02-01T02:03:04Z",
              "elementId": "Task_1",
              "name": "Contract task",
              "altinnTaskType": "data",
              "ended": "2024-02-01T03:04:05Z",
              "validated": {
                "timestamp": "2024-02-01T02:30:00Z",
                "canCompleteTask": true
              },
              "flowType": "CompleteCurrentMoveToNext"
            },
            "ended": "2024-02-02T03:04:05Z",
            "endEvent": "EndEvent_1"
          },
          "status": {
            "isArchived": true,
            "archived": "2024-02-03T04:05:06Z",
            "isSoftDeleted": true,
            "softDeleted": "2024-02-04T05:06:07Z",
            "isHardDeleted": true,
            "hardDeleted": "2024-02-05T06:07:08Z",
            "readStatus": "UpdatedSinceLastReview",
            "substatus": {
              "label": "substatus.label",
              "description": "substatus.description"
            }
          },
          "completeConfirmations": [{
            "stakeholderId": "org",
            "confirmedOn": "2024-02-06T07:08:09Z"
          }],
          "data": [
            {
              "id": "11111111-2222-4333-8444-555555555555",
              "instanceGuid": "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
              "dataType": "contract-data",
              "filename": "contract.pdf",
              "contentType": "application/pdf",
              "blobStoragePath": "org/app/instance/data/element",
              "selfLinks": {
                "apps": "https://apps.example/data",
                "platform": "https://platform.example/data"
              },
              "size": 12345,
              "contentHash": "YWJjZA==",
              "contentEtag": "\"api-content-version\"",
              "locked": true,
              "refs": ["99999999-8888-4777-8666-555555555555"],
              "isRead": false,
              "tags": ["contract", "golden"],
              "userDefinedMetadata": [{"key": "user-key", "value": "user-value"}],
              "metadata": [{"key": "app-key", "value": "app-value"}],
              "deleteStatus": {
                "isHardDeleted": true,
                "hardDeleted": "2024-03-04T05:06:07Z"
              },
              "fileScanResult": "Clean",
              "references": [{
                "value": "Task_1",
                "relation": "GeneratedFrom",
                "valueType": "Task"
              }],
              "created": "2024-01-02T03:04:05Z",
              "createdBy": "created-by",
              "lastChanged": "2024-01-03T04:05:06Z",
              "lastChangedBy": "last-changed-by"
            }
          ],
          "presentationTexts": {"nb": "Kontrakt", "en": "Contract"},
          "dataValues": {"archiveReference": "bdb2a09da7ea", "key": "value"},
          "created": "2024-01-02T03:04:05Z",
          "createdBy": "created-by",
          "lastChanged": "2024-01-03T04:05:06Z",
          "lastChangedBy": "last-changed-by"
        }
        """;

    internal static InstanceInternal CreateDomainInstance()
    {
        DataElementInternal dataElement = CreateDomainDataElement();
        return new InstanceInternal
        {
            Id = InstanceGuid,
            InstanceOwner = CreateInstanceOwner(),
            AppId = "org/contract-app",
            Org = "org",
            DueBefore = Utc(2024, 4, 5, 6, 7, 8),
            VisibleAfter = Utc(2024, 4, 1, 2, 3, 4),
            Process = CreateProcessState(),
            Status = CreateInstanceStatus(),
            CompleteConfirmations =
            [
                new CompleteConfirmation
                {
                    StakeholderId = "org",
                    ConfirmedOn = Utc(2024, 2, 6, 7, 8, 9),
                },
            ],
            Data = [dataElement],
            PresentationTexts = new Dictionary<string, string>
            {
                ["nb"] = "Kontrakt",
                ["en"] = "Contract",
            },
            DataValues = new Dictionary<string, string>
            {
                ["archiveReference"] = "bdb2a09da7ea",
                ["key"] = "value",
            },
            Created = Utc(2024, 1, 2, 3, 4, 5),
            CreatedBy = "created-by",
            LastChanged = Utc(2024, 1, 3, 4, 5, 6),
            LastChangedBy = "last-changed-by",
            Versions = new StorageVersions(7, 11),
            InternalId = 13,
        };
    }

    internal static DataElementInternal CreateDomainDataElement() =>
        new()
        {
            Id = DataElementGuid,
            InstanceGuid = InstanceGuid,
            DataType = "contract-data",
            Filename = "contract.pdf",
            ContentType = "application/pdf",
            BlobStoragePath = "org/app/instance/data/element",
            Size = 12345,
            ContentHash = "YWJjZA==",
            Locked = true,
            Refs = [Guid.Parse("99999999-8888-4777-8666-555555555555")],
            IsRead = false,
            Tags = ["contract", "golden"],
            UserDefinedMetadata = [new KeyValueEntry { Key = "user-key", Value = "user-value" }],
            Metadata = [new KeyValueEntry { Key = "app-key", Value = "app-value" }],
            DeleteStatus = new DeleteStatus
            {
                IsHardDeleted = true,
                HardDeleted = Utc(2024, 3, 4, 5, 6, 7),
            },
            FileScanResult = FileScanResult.Clean,
            References =
            [
                new Reference
                {
                    Value = "Task_1",
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                },
            ],
            Created = Utc(2024, 1, 2, 3, 4, 5),
            CreatedBy = "created-by",
            LastChanged = Utc(2024, 1, 3, 4, 5, 6),
            LastChangedBy = "last-changed-by",
            BlobVersionId = "api-content-version",
        };

    internal static Instance CreateApiInstance(bool apiFormatId)
    {
        DataElement dataElement = CreateApiDataElement();
        return new Instance
        {
            Id = apiFormatId ? $"1337/{InstanceGuid}" : InstanceGuid,
            InstanceOwner = CreateInstanceOwner(),
            AppId = "org/contract-app",
            Org = "org",
            SelfLinks = new ResourceLinks
            {
                Apps = "https://apps.example/instance",
                Platform = "https://platform.example/instance",
            },
            DueBefore = Utc(2024, 4, 5, 6, 7, 8),
            VisibleAfter = Utc(2024, 4, 1, 2, 3, 4),
            Process = CreateProcessState(),
            Status = CreateInstanceStatus(),
            CompleteConfirmations =
            [
                new CompleteConfirmation
                {
                    StakeholderId = "org",
                    ConfirmedOn = Utc(2024, 2, 6, 7, 8, 9),
                },
            ],
            Data = [dataElement],
            PresentationTexts = new Dictionary<string, string>
            {
                ["nb"] = "Kontrakt",
                ["en"] = "Contract",
            },
            DataValues = new Dictionary<string, string>
            {
                ["archiveReference"] = "bdb2a09da7ea",
                ["key"] = "value",
            },
            Created = Utc(2024, 1, 2, 3, 4, 5),
            CreatedBy = "created-by",
            LastChanged = Utc(2024, 1, 3, 4, 5, 6),
            LastChangedBy = "last-changed-by",
        };
    }

    internal static DataElement CreateApiDataElement() =>
        new()
        {
            Id = DataElementGuid,
            InstanceGuid = InstanceGuid,
            DataType = "contract-data",
            Filename = "contract.pdf",
            ContentType = "application/pdf",
            BlobStoragePath = "org/app/instance/data/element",
            SelfLinks = new ResourceLinks
            {
                Apps = "https://apps.example/data",
                Platform = "https://platform.example/data",
            },
            Size = 12345,
            ContentHash = "YWJjZA==",
            ContentEtag = "\"api-content-version\"",
            Locked = true,
            Refs = [Guid.Parse("99999999-8888-4777-8666-555555555555")],
            IsRead = false,
            Tags = ["contract", "golden"],
            UserDefinedMetadata = [new KeyValueEntry { Key = "user-key", Value = "user-value" }],
            Metadata = [new KeyValueEntry { Key = "app-key", Value = "app-value" }],
            DeleteStatus = new DeleteStatus
            {
                IsHardDeleted = true,
                HardDeleted = Utc(2024, 3, 4, 5, 6, 7),
            },
            FileScanResult = FileScanResult.Clean,
            References =
            [
                new Reference
                {
                    Value = "Task_1",
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                },
            ],
            Created = Utc(2024, 1, 2, 3, 4, 5),
            CreatedBy = "created-by",
            LastChanged = Utc(2024, 1, 3, 4, 5, 6),
            LastChangedBy = "last-changed-by",
        };

    private static InstanceOwner CreateInstanceOwner() =>
        new()
        {
            PartyId = "1337",
            PersonNumber = "01010112345",
            OrganisationNumber = "999888777",
            Username = "contract-user",
            ExternalIdentifier = "external-user",
        };

    private static ProcessState CreateProcessState() =>
        new()
        {
            Started = Utc(2024, 2, 1, 1, 2, 3),
            StartEvent = "StartEvent_1",
            CurrentTask = CreateProcessElementInfo(),
            Ended = Utc(2024, 2, 2, 3, 4, 5),
            EndEvent = "EndEvent_1",
        };

    private static InstanceStatus CreateInstanceStatus() =>
        new()
        {
            IsArchived = true,
            Archived = Utc(2024, 2, 3, 4, 5, 6),
            IsSoftDeleted = true,
            SoftDeleted = Utc(2024, 2, 4, 5, 6, 7),
            IsHardDeleted = true,
            HardDeleted = Utc(2024, 2, 5, 6, 7, 8),
            ReadStatus = ReadStatus.UpdatedSinceLastReview,
            Substatus = new Substatus
            {
                Label = "substatus.label",
                Description = "substatus.description",
            },
        };

#pragma warning disable CS0618
    private static ProcessElementInfo CreateProcessElementInfo() =>
        new()
        {
            Flow = 7,
            Started = Utc(2024, 2, 1, 2, 3, 4),
            ElementId = "Task_1",
            Name = "Contract task",
            AltinnTaskType = "data",
            Ended = Utc(2024, 2, 1, 3, 4, 5),
            Validated = new ValidationStatus
            {
                Timestamp = Utc(2024, 2, 1, 2, 30, 0),
                CanCompleteTask = true,
            },
            FlowType = "CompleteCurrentMoveToNext",
        };
#pragma warning restore CS0618

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
