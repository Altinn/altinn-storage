#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static Altinn.Platform.Storage.Interface.Models.SignRequest;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class StorageAtomicSequencingRegressionTests : IClassFixture<StorageAtomicSequencingFixture>
{
    private const int PartyId = 1337;
    private const int UserId = 20001;
    private const string TargetTask = "Task_2";
    private const string SignatureDataType = "signature";
    private const string SignedDataType = "model";

    private readonly StorageAtomicSequencingFixture _fixture;

    public StorageAtomicSequencingRegressionTests(StorageAtomicSequencingFixture fixture)
    {
        _fixture = fixture;
        string sql =
            "delete from storage.dataelementblobversions; delete from storage.instanceevents; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    [Fact]
    public async Task PutInstanceAndEvents_WithGeneratedCleanupAndMatchingVersions_DoesNotSelfConflict()
    {
        Instance instance = await CreateInstance();
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        long instanceInternalId = await ReadInstanceInternalId(instanceGuid);
        InMemoryBlobRepository blobRepository = new();
        DataElement staleGenerated = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            dataType: "receipt",
            references:
            [
                new Reference
                {
                    Relation = RelationType.GeneratedFrom,
                    ValueType = ReferenceType.Task,
                    Value = TargetTask,
                },
            ]
        );
        StorageVersions versions = await ReadVersions(instanceGuid);

        ProcessController controller = CreateProcessController(blobRepository);
        SetHttpContext(controller, versions);

        ActionResult<Instance> result = await controller.PutInstanceAndEvents(
            PartyId,
            instanceGuid,
            new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo
                    {
                        ElementId = TargetTask,
                        AltinnTaskType = "data",
                    },
                },
                Events = [],
            },
            deleteGeneratedElements: null,
            CancellationToken.None
        );
        int staleRows = await CountDataElementRows(staleGenerated.Id);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int deletedEvents = await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted);

        Assert.True(
            result.Result is OkObjectResult && staleRows == 0 && deletedEvents == 1,
            $"Expected process update to succeed and remove stale generated data with a transactional Deleted event. Actual result was {DescribeActionResult(result.Result)}, stale row count was {staleRows}, Deleted event count was {deletedEvents}, instance version moved from {versions.InstanceVersion} to {currentInstanceVersion}."
        );
    }

    [Fact]
    public async Task Sign_WithExistingSignatureAndMatchingVersions_ReplacesAtomically()
    {
        Instance instance = await CreateInstance(currentTaskType: "signing");
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        long instanceInternalId = await ReadInstanceInternalId(instanceGuid);
        InMemoryBlobRepository blobRepository = new();

        DataElement signedData = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            SignedDataType,
            blobContent: "payload to sign"u8.ToArray()
        );
        Signee signee = new() { UserId = "1337", PersonNumber = "22117612345" };
        SignDocument existingSignature = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = DateTime.UtcNow.AddMinutes(-5),
            SigneeInfo = signee,
            DataElementSignatures = [],
        };
        DataElement oldSignature = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            SignatureDataType,
            blobContent: JsonSerializer.SerializeToUtf8Bytes(existingSignature)
        );
        StorageVersions versions = await ReadVersions(instanceGuid);

        SignController controller = CreateSignController(blobRepository);
        SetHttpContext(controller, versions);

        Exception exception = await Record.ExceptionAsync(async () =>
        {
            ActionResult result = await controller.Sign(
                PartyId,
                instanceGuid,
                new SignRequest
                {
                    SignatureDocumentDataType = SignatureDataType,
                    DataElementSignatures =
                    [
                        new DataElementSignature { DataElementId = signedData.Id, Signed = true },
                    ],
                    Signee = signee,
                    GeneratedFromTask = TargetTask,
                },
                CancellationToken.None
            );

            Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)result).StatusCode);
        });
        int oldSignatureRows = await CountDataElementRows(oldSignature.Id);
        int replacementRows = await CountDataElementsByType(instanceGuid, SignatureDataType);
        int signedEvents = await CountInstanceEvents(instanceGuid, InstanceEventType.Signed);
        int deletedEvents = await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted);
        string signatureLastChangedBy = await ReadDataElementLastChangedBy(
            instanceGuid,
            SignatureDataType
        );
        string instanceLastChangedBy = await ReadInstanceLastChangedBy(instanceGuid);

        Assert.True(
            exception is null
                && oldSignatureRows == 0
                && replacementRows == 1
                && signedEvents == 1
                && deletedEvents == 1
                && signatureLastChangedBy == UserId.ToString()
                && instanceLastChangedBy == UserId.ToString(),
            $"Expected signing replacement to create the new signature, remove the old one, commit Signed plus Deleted events atomically, and attribute both the signature and the instance to the signee. Actual exception was {DescribeException(exception)}, old signature row count was {oldSignatureRows}, signature row count was {replacementRows}, Signed event count was {signedEvents}, Deleted event count was {deletedEvents}, signature LastChangedBy was {signatureLastChangedBy}, instance LastChangedBy was {instanceLastChangedBy}."
        );
    }

    [Fact]
    public async Task Sign_WithDuplicateSignaturesForSignee_CollapsesToASingleSignature()
    {
        Instance instance = await CreateInstance(currentTaskType: "signing");
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        long instanceInternalId = await ReadInstanceInternalId(instanceGuid);
        InMemoryBlobRepository blobRepository = new();

        DataElement signedData = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            SignedDataType,
            blobContent: "payload to sign"u8.ToArray()
        );
        Signee signee = new() { UserId = "1337", PersonNumber = "22117612345" };
        List<DataElement> duplicateSignatures = [];
        for (int index = 0; index < 3; index++)
        {
            SignDocument duplicate = new()
            {
                Id = Guid.NewGuid().ToString(),
                InstanceGuid = instanceGuid.ToString(),
                SignedTime = DateTime.UtcNow.AddMinutes(-5 + index),
                SigneeInfo = signee,
                DataElementSignatures = [],
            };
            duplicateSignatures.Add(
                await CreateDataElement(
                    instance,
                    instanceInternalId,
                    blobRepository,
                    SignatureDataType,
                    blobContent: JsonSerializer.SerializeToUtf8Bytes(duplicate)
                )
            );
        }

        StorageVersions versions = await ReadVersions(instanceGuid);
        SignController controller = CreateSignController(blobRepository);
        SetHttpContext(controller, versions);

        Exception exception = await Record.ExceptionAsync(async () =>
        {
            ActionResult result = await controller.Sign(
                PartyId,
                instanceGuid,
                new SignRequest
                {
                    SignatureDocumentDataType = SignatureDataType,
                    DataElementSignatures =
                    [
                        new DataElementSignature { DataElementId = signedData.Id, Signed = true },
                    ],
                    Signee = signee,
                    GeneratedFromTask = TargetTask,
                },
                CancellationToken.None
            );

            Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)result).StatusCode);
        });
        int survivingDuplicateRows = 0;
        foreach (DataElement duplicateSignature in duplicateSignatures)
        {
            survivingDuplicateRows += await CountDataElementRows(duplicateSignature.Id);
        }

        int signatureRows = await CountDataElementsByType(instanceGuid, SignatureDataType);
        int deletedEvents = await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted);
        int instanceVersion = await ReadInstanceVersion(instanceGuid);

        Assert.True(
            exception is null
                && survivingDuplicateRows == 0
                && signatureRows == 1
                && deletedEvents == duplicateSignatures.Count
                && instanceVersion == versions.InstanceVersion + 1,
            $"Expected signing to sweep every pre-existing signature for the signee and leave exactly one, in a single version bump. Actual exception was {DescribeException(exception)}, surviving duplicate row count was {survivingDuplicateRows}, signature row count was {signatureRows}, Deleted event count was {deletedEvents}, instance version moved from {versions.InstanceVersion} to {instanceVersion}."
        );
    }

    [Fact]
    public async Task Sign_WithExistingSignatureAndStaleVersions_PreservesOldSignatureAndBlob()
    {
        Instance instance = await CreateInstance(currentTaskType: "signing");
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        long instanceInternalId = await ReadInstanceInternalId(instanceGuid);
        InMemoryBlobRepository blobRepository = new();

        DataElement signedData = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            SignedDataType,
            blobContent: "payload to sign"u8.ToArray()
        );
        Signee signee = new() { UserId = "1337", PersonNumber = "22117612345" };
        SignDocument existingSignature = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = DateTime.UtcNow.AddMinutes(-5),
            SigneeInfo = signee,
            DataElementSignatures = [],
        };
        DataElement oldSignature = await CreateDataElement(
            instance,
            instanceInternalId,
            blobRepository,
            SignatureDataType,
            blobContent: JsonSerializer.SerializeToUtf8Bytes(existingSignature)
        );
        StorageVersions staleVersions = await ReadVersions(instanceGuid);
        await BumpInstanceVersion(instanceGuid);

        SignController controller = CreateSignController(blobRepository);
        SetHttpContext(controller, staleVersions);

        ActionResult result = await controller.Sign(
            PartyId,
            instanceGuid,
            new SignRequest
            {
                SignatureDocumentDataType = SignatureDataType,
                DataElementSignatures =
                [
                    new DataElementSignature { DataElementId = signedData.Id, Signed = true },
                ],
                Signee = signee,
                GeneratedFromTask = TargetTask,
            },
            CancellationToken.None
        );

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, objectResult.StatusCode);
        Assert.Equal(1, await CountDataElementRows(oldSignature.Id));
        Assert.Equal(1, await CountDataElementsByType(instanceGuid, SignatureDataType));
        Assert.Equal(2, await CountBlobVersionRows());
        Assert.Equal(0, await CountInstanceEvents(instanceGuid, InstanceEventType.Signed));
        Assert.Equal(0, await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted));
        await using Stream oldBlob = await blobRepository.ReadBlob(
            instance.Org,
            oldSignature.BlobStoragePath,
            null,
            CancellationToken.None
        );
        Assert.NotNull(oldBlob);
    }

    private ProcessController CreateProcessController(InMemoryBlobRepository blobRepository)
    {
        ProcessDataCleanupService cleanupService = new(
            NullLogger<ProcessDataCleanupService>.Instance
        );
        DataService dataService = CreateDataService(blobRepository);
        Mock<IProcessAuthorizer> processAuthorizer = new();
        processAuthorizer
            .Setup(a =>
                a.AuthorizeProcessNext(It.IsAny<InstanceInternal>(), It.IsAny<ProcessState>())
            )
            .ReturnsAsync(true);

        return new ProcessController(
            _fixture.InstanceRepo,
            Mock.Of<IInstanceEventRepository>(),
            _fixture.InstanceMutationRepo,
            Options.Create(new GeneralSettings { Hostname = "http://localhost" }),
            processAuthorizer.Object,
            CreateInstanceEventService().Object,
            cleanupService,
            CreateApplicationService().Object,
            dataService
        );
    }

    private SignController CreateSignController(InMemoryBlobRepository blobRepository)
    {
        SigningService signingService = new(
            _fixture.InstanceRepo,
            CreateDataService(blobRepository),
            CreateApplicationService().Object,
            CreateInstanceEventService().Object,
            _fixture.InstanceMutationRepo,
            CreateApplicationRepository().Object,
            blobRepository,
            NullLogger<SigningService>.Instance
        );

        return new SignController(signingService);
    }

    private DataService CreateDataService(InMemoryBlobRepository blobRepository)
    {
        return new DataService(Mock.Of<IFileScanQueueClient>(), _fixture.DataRepo, blobRepository);
    }

    private static Mock<IApplicationService> CreateApplicationService()
    {
        Mock<IApplicationService> applicationService = new();
        applicationService
            .Setup(s => s.GetApplicationOrErrorAsync(It.IsAny<string>()))
            .ReturnsAsync((new Application { StorageAccountNumber = null }, null));
        applicationService
            .Setup(s =>
                s.ValidateDataTypeForApp(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync((true, null));

        return applicationService;
    }

    private static Mock<IApplicationRepository> CreateApplicationRepository()
    {
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(r =>
                r.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new Application { StorageAccountNumber = null });

        return applicationRepository;
    }

    private static Mock<IInstanceEventService> CreateInstanceEventService()
    {
        Mock<IInstanceEventService> instanceEventService = new();
        instanceEventService
            .Setup(s =>
                s.BuildInstanceEvent(It.IsAny<InstanceEventType>(), It.IsAny<InstanceInternal>())
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal targetInstance) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        InstanceId = $"{targetInstance.InstanceOwner.PartyId}/{targetInstance.Id}",
                        User = new PlatformUser { UserId = 1337 },
                    }
            );
        instanceEventService
            .Setup(s =>
                s.BuildInstanceEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (
                    InstanceEventType eventType,
                    InstanceInternal targetInstance,
                    DataElementInternal dataElement
                ) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        InstanceId = $"{targetInstance.InstanceOwner.PartyId}/{targetInstance.Id}",
                        DataId = dataElement.Id,
                        User = new PlatformUser { UserId = 1337 },
                    }
            );

        return instanceEventService;
    }

    private async Task<Instance> CreateInstance(string currentTaskType = "data")
    {
        Guid instanceGuid = Guid.NewGuid();
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Id = $"{PartyId}/{instanceGuid}";
        instance.InstanceOwner.PartyId = PartyId.ToString();
        instance.Data = [];
        instance.Process.CurrentTask = new ProcessElementInfo
        {
            ElementId = "Task_1",
            AltinnTaskType = currentTaskType,
        };

        return (
            await _fixture.InstanceRepo.Create(instance.FromApiModel(), CancellationToken.None)
        ).ToApiModel();
    }

    private async Task<DataElement> CreateDataElement(
        Instance instance,
        long instanceInternalId,
        InMemoryBlobRepository blobRepository,
        string dataType,
        IReadOnlyList<Reference> references = null,
        byte[] blobContent = null
    )
    {
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        Guid dataElementId = Guid.NewGuid();
        string blobVersionId = await _fixture.DataRepo.CreateBlobVersionId(
            instanceGuid,
            dataElementId,
            instance.AppId,
            instance.Org,
            null,
            CancellationToken.None
        );
        string blobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instanceGuid.ToString(),
            blobVersionId
        );
        blobRepository.Put(
            instance.Org,
            blobStoragePath,
            blobContent ?? "test content"u8.ToArray()
        );

        DataElement dataElement = new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            DataType = dataType,
            ContentType = "application/json",
            CreatedBy = "setup",
            Created = DateTime.UtcNow,
            LastChangedBy = "setup",
            LastChanged = DateTime.UtcNow,
            Size = blobContent?.Length ?? "test content"u8.Length,
            BlobStoragePath = blobStoragePath,
            References = references?.ToList(),
        };

        return (
            await _fixture.DataRepo.Create(
                dataElement.FromApiModel(blobVersionId),
                instanceInternalId,
                CancellationToken.None
            )
        ).DataElement.ToApiModel();
    }

    private static void SetHttpContext(ControllerBase controller, StorageVersions versions)
    {
        DefaultHttpContext httpContext = new()
        {
            User = PrincipalUtil.GetPrincipal(UserId, PartyId, 3),
        };
        httpContext.Request.Headers[StorageHeaders.IfInstanceVersionMatch] =
            versions.InstanceVersion.ToString();
        httpContext.Request.Headers[StorageHeaders.IfProcessStateVersionMatch] =
            versions.ProcessStateVersion.ToString();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static string DescribeActionResult(ActionResult result)
    {
        return result switch
        {
            ObjectResult objectResult =>
                $"{objectResult.GetType().Name} status {objectResult.StatusCode} ({DescribeProblem(objectResult.Value)})",
            StatusCodeResult statusCodeResult =>
                $"{statusCodeResult.GetType().Name} status {statusCodeResult.StatusCode}",
            null => "null",
            _ => result.GetType().Name,
        };
    }

    private static string DescribeProblem(object value)
    {
        return value is ProblemDetails problem
            ? $"{problem.Type}: {problem.Title}"
            : value?.ToString() ?? "no body";
    }

    private static string DescribeException(Exception exception)
    {
        return exception is null ? "none" : $"{exception.GetType().Name}: {exception.Message}";
    }

    private static async Task<StorageVersions> ReadVersions(Guid instanceGuid)
    {
        return new StorageVersions(
            await ReadInstanceVersion(instanceGuid),
            await ReadProcessStateVersion(instanceGuid)
        );
    }

    private static Task<int> ReadInstanceVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<int>(
            $"select instance_version from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<int> ReadProcessStateVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<int>(
            $"select process_state_version from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<long> ReadInstanceInternalId(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<long>(
            $"select id from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<int> CountDataElementRows(string dataElementId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{dataElementId}'"
        );
    }

    private static Task<int> CountDataElementsByType(Guid instanceGuid, string dataType)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where instanceguid = '{instanceGuid}' and element ->> 'DataType' = '{dataType}'"
        );
    }

    private static Task<string> ReadDataElementLastChangedBy(Guid instanceGuid, string dataType)
    {
        return PostgresUtil.RunQuery<string>(
            $"select coalesce(max(element ->> 'LastChangedBy'), '<none>') from storage.dataelements where instanceguid = '{instanceGuid}' and element ->> 'DataType' = '{dataType}'"
        );
    }

    private static Task<string> ReadInstanceLastChangedBy(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<string>(
            $"select coalesce(max(instance ->> 'LastChangedBy'), '<none>') from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<int> CountBlobVersionRows()
    {
        return PostgresUtil.RunCountQuery("select count(*) from storage.dataelementblobversions");
    }

    private static Task<int> CountInstanceEvents(Guid instanceGuid, InstanceEventType eventType)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instanceevents where instance = '{instanceGuid}' and event ->> 'EventType' = '{eventType}'"
        );
    }

    private static Task BumpInstanceVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance_version = instance_version + 1 where alternateid = '{instanceGuid}'"
        );
    }

    private sealed class InMemoryBlobRepository : IBlobRepository
    {
        private readonly ConcurrentDictionary<string, byte[]> _content = new();

        public void Put(string org, string blobStoragePath, byte[] content)
        {
            _content[Key(org, blobStoragePath)] = content;
        }

        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
            string org,
            Stream stream,
            string blobStoragePath,
            int? storageAccountNumber
        )
        {
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer);
            byte[] content = buffer.ToArray();
            Put(org, blobStoragePath, content);
            return (content.Length, DateTimeOffset.UtcNow);
        }

        public Task<Stream> ReadBlob(
            string org,
            string blobStoragePath,
            int? storageAccountNumber,
            CancellationToken cancellationToken = default
        )
        {
            if (!_content.TryGetValue(Key(org, blobStoragePath), out byte[] content))
            {
                return Task.FromResult<Stream>(null);
            }

            return Task.FromResult<Stream>(new MemoryStream(content));
        }

        public Task<bool> DeleteBlob(string org, string blobStoragePath, int? storageAccountNumber)
        {
            _content.TryRemove(Key(org, blobStoragePath), out _);
            return Task.FromResult(true);
        }

        public Task<bool[]> DeleteBlobsIfExists(
            string org,
            IReadOnlyList<string> blobStoragePaths,
            int? storageAccountNumber,
            CancellationToken cancellationToken = default
        )
        {
            bool[] result = new bool[blobStoragePaths.Count];
            int index = 0;
            foreach (string blobStoragePath in blobStoragePaths)
            {
                _content.TryRemove(Key(org, blobStoragePath), out _);
                result[index++] = true;
            }

            return Task.FromResult(result);
        }

        public Task<bool> DeleteDataBlobs(
            string org,
            string appId,
            string instanceGuid,
            int? storageAccountNumber,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(true);
        }

        private static string Key(string org, string blobStoragePath) => $"{org}/{blobStoragePath}";
    }
}

public class StorageAtomicSequencingFixture
{
    public IInstanceRepository InstanceRepo { get; }

    public IInstanceMutationRepository InstanceMutationRepo { get; }

    public IDataRepository DataRepo { get; }

    public StorageAtomicSequencingFixture()
    {
        List<object> serviceList = ServiceUtil.GetServices(
            new List<Type>
            {
                typeof(IInstanceRepository),
                typeof(IInstanceMutationRepository),
                typeof(IDataRepository),
            }
        );
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        InstanceMutationRepo = (IInstanceMutationRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceMutationRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
    }
}
