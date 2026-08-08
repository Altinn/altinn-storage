#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.TestingRepositories;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

[Collection("StoragePostgreSQL")]
public class MigrationControllerTests : IClassFixture<InstanceFixture>
{
    private const int A2ArchiveReference = 4242;
    private readonly InstanceFixture _instanceFixture;

    public MigrationControllerTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;
        _ = PostgresUtil
            .RunSql(
                "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;"
            )
            .Result;
    }

    [Theory]
    [InlineData(
        "legacy-prefix/045ea5db-6dd4-4476-b774-bdb2a09da7ea",
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea",
        null,
        "<absent>"
    )]
    [InlineData(
        "9999/145ea5db-6dd4-4476-b774-bdb2a09da7ea/legacy-suffix",
        "145ea5db-6dd4-4476-b774-bdb2a09da7ea",
        ProcessStatus.Idle,
        "\"idle\""
    )]
    [InlineData(
        "9999/245ea5db-6dd4-4476-b774-bdb2a09da7ea/legacy-suffix",
        "245ea5db-6dd4-4476-b774-bdb2a09da7ea",
        ProcessStatus.Processing,
        "\"processing\""
    )]
    public async Task CreateInstance_LegacyCompositeId_UsesHistoricalStorageTranslation(
        string incomingId,
        string expectedStorageId,
        string processStatus,
        string expectedStoredStatus
    )
    {
        Instance incoming = TestData.Instance_1_1.Clone();
        incoming.Id = incomingId;
        incoming.DataValues = new Dictionary<string, string>
        {
            ["A2ArchRef"] = A2ArchiveReference.ToString(),
        };
        incoming.Process.Status = processStatus;
        incoming.Process.CurrentTask.Name = "migration-process-preserved";
        Mock<IA2Repository> a2Repository = new();
        a2Repository
            .Setup(repository => repository.GetA2MigrationInstanceId(A2ArchiveReference))
            .ReturnsAsync((Guid?)null);
        a2Repository
            .Setup(repository => repository.CreateA2MigrationState(A2ArchiveReference))
            .Returns(Task.CompletedTask);
        a2Repository
            .Setup(repository =>
                repository.UpdateStartA2MigrationState(
                    A2ArchiveReference,
                    Guid.Parse(expectedStorageId)
                )
            )
            .Returns(Task.CompletedTask);
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        MigrationController controller = CreateController(a2Repository.Object, memoryCache);

        ActionResult<Instance> result = await controller.CreateInstance(
            incoming,
            CancellationToken.None
        );

        CreatedResult created = Assert.IsType<CreatedResult>(result.Result);
        Instance response = Assert.IsType<Instance>(created.Value);
        Assert.Equal($"{incoming.InstanceOwner.PartyId}/{expectedStorageId}", response.Id);
        Assert.Equal(processStatus, response.Process.Status);
        Assert.Equal("migration-process-preserved", response.Process.CurrentTask.Name);
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(expectedStorageId),
            false,
            CancellationToken.None
        );
        Assert.Equal(expectedStorageId, persisted.Id.ToString());
        Assert.Equal(processStatus, persisted.Process.Status);
        Assert.Equal("migration-process-preserved", persisted.Process.CurrentTask.Name);
        Assert.Equal(
            expectedStoredStatus,
            await PostgresUtil.RunQuery<string>(
                $"select case when instance -> 'Process' ? 'Status' then (instance -> 'Process' -> 'Status')::text else '<absent>' end from storage.instances where alternateid = '{expectedStorageId}'"
            )
        );
        a2Repository.VerifyAll();
    }

    [Theory]
    [InlineData("future-status")]
    [InlineData("Idle")]
    [InlineData("Processing")]
    [InlineData("idle ")]
    [InlineData(" processing")]
    public async Task CreateInstance_UnsupportedProcessStatus_ReturnsBadRequestBeforeMigrationWork(
        string processStatus
    )
    {
        Instance incoming = TestData.Instance_1_1.Clone();
        incoming.Process.Status = processStatus;
        Mock<IA2Repository> a2Repository = new();
        Mock<IInstanceRepository> instanceRepository = new();
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        MigrationController controller = CreateController(
            a2Repository.Object,
            memoryCache,
            instanceRepository.Object
        );

        ActionResult<Instance> result = await controller.CreateInstance(
            incoming,
            CancellationToken.None
        );

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        string message = Assert.IsType<string>(badRequest.Value);
        Assert.Contains("process.status", message);
        Assert.Contains(ProcessStatus.Idle, message);
        Assert.Contains(ProcessStatus.Processing, message);
        instanceRepository.VerifyNoOtherCalls();
        a2Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateDataElement_ProcessStatusConflict_CompensatesStagedBlobAndPreservesConflictWhenVersionCleanupFails()
    {
        Guid instanceGuid = Guid.NewGuid();
        string allocatedBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Id = instanceGuid;
        instance.InternalId = 42;
        instance.AppId = "a2-process-status-test";
        Mock<IInstanceRepository> instanceRepository = new();
        instanceRepository
            .Setup(repository =>
                repository.GetOne(instanceGuid, false, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(instance);
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(repository =>
                repository.FindOne(instance.AppId, instance.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new Application
                {
                    Id = $"{instance.Org}/a2-process-status-test",
                    Org = instance.Org,
                }
            );
        Mock<IDataRepository> dataRepository = new();
        dataRepository
            .Setup(repository =>
                repository.CreateBlobVersionId(
                    instanceGuid,
                    It.Is<Guid>(dataElementId => dataElementId != Guid.Empty),
                    instance.AppId,
                    instance.Org,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allocatedBlobVersionId);
        dataRepository
            .Setup(repository =>
                repository.Create(
                    It.Is<DataElementInternal>(dataElement =>
                        dataElement.BlobVersionId == allocatedBlobVersionId
                    ),
                    instance.InternalId,
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing));
        dataRepository
            .Setup(repository =>
                repository.DeleteBlobVersion(
                    It.Is<Guid>(dataElementId => dataElementId != Guid.Empty),
                    allocatedBlobVersionId,
                    CancellationToken.None
                )
            )
            .ThrowsAsync(new InvalidOperationException("version cleanup failed"));
        string expectedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instanceGuid,
            allocatedBlobVersionId
        );
        Mock<IBlobRepository> blobRepository = new();
        blobRepository
            .Setup(repository =>
                repository.WriteBlob(
                    instance.Org,
                    It.IsAny<Stream>(),
                    expectedBlobStoragePath,
                    null
                )
            )
            .ReturnsAsync((3L, DateTimeOffset.UtcNow));
        blobRepository
            .Setup(repository => repository.DeleteBlob(instance.Org, expectedBlobStoragePath, null))
            .ReturnsAsync(true);
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        MigrationController controller = CreateController(
            Mock.Of<IA2Repository>(),
            memoryCache,
            instanceRepository.Object,
            dataRepository.Object,
            applicationRepository.Object,
            blobRepository.Object
        );
        byte[] requestBody = [1, 2, 3];
        DefaultHttpContext httpContext = new();
        httpContext.Request.Body = new MemoryStream(requestBody);
        httpContext.Request.ContentLength = requestBody.Length;
        httpContext.Request.ContentType = "application/octet-stream";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        ActionResult<DataElement> result = await controller.CreateDataElement(
            instanceGuid,
            DateTime.UtcNow.Ticks,
            DateTime.UtcNow.Ticks,
            "binary-data",
            null,
            null,
            null,
            null,
            CancellationToken.None
        );

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains(
            ProcessStatus.Processing,
            Assert.IsType<string>(conflict.Value),
            StringComparison.Ordinal
        );
        dataRepository.VerifyAll();
        blobRepository.VerifyAll();
    }

    private MigrationController CreateController(
        IA2Repository a2Repository,
        IMemoryCache memoryCache,
        IInstanceRepository instanceRepository = null,
        IDataRepository dataRepository = null,
        IApplicationRepository applicationRepository = null,
        IBlobRepository blobRepository = null
    )
    {
        GeneralSettings settings = new() { PdfGeneratorEndpoint = "http://localhost/" };
        return new MigrationController(
            instanceRepository ?? _instanceFixture.InstanceRepo,
            Mock.Of<IInstanceEventRepository>(),
            dataRepository ?? Mock.Of<IDataRepository>(),
            blobRepository ?? Mock.Of<IBlobRepository>(),
            applicationRepository ?? Mock.Of<IApplicationRepository>(),
            a2Repository,
            Mock.Of<ITextRepository>(),
            NullLogger<MigrationController>.Instance,
            Options.Create(settings),
            Options.Create(new AzureStorageConfiguration()),
            Options.Create(settings),
            new HttpClient(),
            memoryCache
        );
    }
}
