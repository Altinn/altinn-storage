using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class DataRepositoryMockTests
{
    [Fact]
    public async Task ReadBlobVersions_GroupsVersionsByStoredContext()
    {
        DataRepositoryMock repository = new();
        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();

        string firstBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId
        );
        string otherContextBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            "other/app",
            "other",
            null
        );
        string secondBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId
        );

        await repository.Create(CreateDataElement(instanceGuid, dataElementId, firstBlobVersionId));
        await AttachBlobVersion(repository, instanceGuid, dataElementId, otherContextBlobVersionId);
        await AttachBlobVersion(repository, instanceGuid, dataElementId, secondBlobVersionId);

        var references = await repository.ReadBlobVersions(dataElementId);

        Assert.Collection(
            references,
            firstContext =>
            {
                Assert.Equal(instanceGuid, firstContext.InstanceGuid);
                Assert.Equal("ttd/app", firstContext.AppId);
                Assert.Equal("ttd", firstContext.BlobStorageOrg);
                Assert.Equal(42, firstContext.StorageAccountNumber);
                Assert.Equal(
                    [firstBlobVersionId, secondBlobVersionId],
                    firstContext.BlobVersionIds
                );
            },
            secondContext =>
            {
                Assert.Equal(instanceGuid, secondContext.InstanceGuid);
                Assert.Equal("other/app", secondContext.AppId);
                Assert.Equal("other", secondContext.BlobStorageOrg);
                Assert.Null(secondContext.StorageAccountNumber);
                Assert.Equal([otherContextBlobVersionId], secondContext.BlobVersionIds);
            }
        );
    }

    [Fact]
    public async Task DeleteBlobVersion_DeletesOnlyDetachedVersionsAndReturnsActualResult()
    {
        DataRepositoryMock repository = new();
        Guid dataElementId = Guid.NewGuid();
        Guid instanceGuid = Guid.NewGuid();

        string attachedBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId
        );
        string detachedBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            "other/app",
            "other",
            null
        );

        await repository.Create(
            CreateDataElement(instanceGuid, dataElementId, attachedBlobVersionId)
        );

        Assert.False(await repository.DeleteBlobVersion(dataElementId, attachedBlobVersionId));
        Assert.True(await repository.DeleteBlobVersion(dataElementId, detachedBlobVersionId));
        Assert.False(await repository.DeleteBlobVersion(dataElementId, detachedBlobVersionId));
        Assert.False(await repository.DeleteBlobVersion(dataElementId, null!));
        Assert.False(await repository.DeleteBlobVersion(dataElementId, string.Empty));
        Assert.False(
            await repository.DeleteBlobVersion(dataElementId, BlobVersionId.Encode(Guid.NewGuid()))
        );
        RepositoryException malformedException = await Assert.ThrowsAsync<RepositoryException>(() =>
            repository.DeleteBlobVersion(dataElementId, "not-a-blob-version-id")
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformedException.StatusCodeSuggestion);
        Assert.Equal(
            "Blob version id 'not-a-blob-version-id' is not valid.",
            malformedException.Message
        );
        Assert.IsType<FormatException>(malformedException.InnerException);

        BlobVersionReferencesInternal attachedGroup = Assert.Single(
            await repository.ReadBlobVersions(dataElementId)
        );
        Assert.Equal([attachedBlobVersionId], attachedGroup.BlobVersionIds);

        Guid detachedOnlyElementId = Guid.NewGuid();
        string lastDetachedBlobVersionId = await AllocateBlobVersion(
            repository,
            instanceGuid,
            detachedOnlyElementId
        );

        Assert.True(
            await repository.DeleteBlobVersion(detachedOnlyElementId, lastDetachedBlobVersionId)
        );
        Assert.False(
            await repository.DeleteBlobVersion(detachedOnlyElementId, lastDetachedBlobVersionId)
        );
        Assert.Empty(await repository.ReadBlobVersions(detachedOnlyElementId));
    }

    [Fact]
    public async Task UpdateFileScanStatus_AcceptsCanonicalVersionAndRejectsMalformedInput()
    {
        DataRepositoryMock repository = new();
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string blobVersionId = await AllocateBlobVersion(repository, instanceGuid, dataElementId);

        DataElementInternal createdElement = await repository.Create(
            CreateDataElement(instanceGuid, dataElementId, blobVersionId)
        );
        DataElementInternal updatedElement = await repository.UpdateFileScanStatus(
            instanceGuid,
            dataElementId,
            new FileScanStatus
            {
                BlobVersionId = blobVersionId,
                FileScanResult = FileScanResult.Clean,
            }
        );

        Assert.Equal(blobVersionId, createdElement.BlobVersionId);
        Assert.Equal(
            blobVersionId,
            (await repository.Read(instanceGuid, dataElementId)).BlobVersionId
        );
        Assert.Equal(blobVersionId, updatedElement.BlobVersionId);
        Assert.Equal(FileScanResult.Clean, updatedElement.FileScanResult);

        RepositoryException malformedException = await Assert.ThrowsAsync<RepositoryException>(() =>
            repository.UpdateFileScanStatus(
                instanceGuid,
                dataElementId,
                new FileScanStatus
                {
                    BlobVersionId = "not-a-blob-version-id",
                    FileScanResult = FileScanResult.Infected,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformedException.StatusCodeSuggestion);
        Assert.IsType<FormatException>(malformedException.InnerException);
        Assert.Equal(
            FileScanResult.Clean,
            (await repository.Read(instanceGuid, dataElementId)).FileScanResult
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_WithVersionAllocatedToDifferentOwner_RejectsWithoutMutation(
        bool wrongInstance
    )
    {
        DataRepositoryMock repository = new();
        Guid requestedInstanceGuid = Guid.NewGuid();
        Guid requestedDataElementId = Guid.NewGuid();
        Guid allocationInstanceGuid = wrongInstance ? Guid.NewGuid() : requestedInstanceGuid;
        Guid allocationDataElementId = wrongInstance ? requestedDataElementId : Guid.NewGuid();

        string blobVersionId = await AllocateBlobVersion(
            repository,
            allocationInstanceGuid,
            allocationDataElementId
        );

        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            repository.Create(
                CreateDataElement(requestedInstanceGuid, requestedDataElementId, blobVersionId)
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(
            $"Blob version {blobVersionId} is not available for data element {requestedDataElementId}.",
            exception.Message
        );
        Assert.Null(await repository.Read(requestedInstanceGuid, requestedDataElementId));
        Assert.Empty(await repository.ReadBlobVersions(requestedDataElementId));
        Assert.True(await repository.DeleteBlobVersion(allocationDataElementId, blobVersionId));
    }

    [Fact]
    public async Task Mutations_WithWrongInstance_RejectWithoutChangingElementOrBlobVersions()
    {
        DataRepositoryMock repository = new();
        Guid ownerInstanceGuid = Guid.NewGuid();
        Guid otherInstanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string currentVersion = await AllocateBlobVersion(
            repository,
            ownerInstanceGuid,
            dataElementId
        );
        string otherInstanceVersion = await AllocateBlobVersion(
            repository,
            otherInstanceGuid,
            dataElementId
        );
        await repository.Create(
            CreateDataElement(ownerInstanceGuid, dataElementId, currentVersion)
        );

        RepositoryException updateException = await Assert.ThrowsAsync<RepositoryException>(() =>
            AttachBlobVersion(repository, otherInstanceGuid, dataElementId, otherInstanceVersion)
        );
        DataElementInternal scanResult = await repository.UpdateFileScanStatus(
            otherInstanceGuid,
            dataElementId,
            new FileScanStatus
            {
                BlobVersionId = currentVersion,
                FileScanResult = FileScanResult.Infected,
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, updateException.StatusCodeSuggestion);
        Assert.Equal($"Data element {dataElementId} was not found.", updateException.Message);
        Assert.Null(scanResult);
        DataElementInternal storedElement = await repository.Read(ownerInstanceGuid, dataElementId);
        Assert.Equal(currentVersion, storedElement.BlobVersionId);
        Assert.NotEqual(FileScanResult.Infected, storedElement.FileScanResult);
        Assert.Equal(
            [currentVersion],
            Assert.Single(await repository.ReadBlobVersions(dataElementId)).BlobVersionIds
        );
        Assert.True(await repository.DeleteBlobVersion(dataElementId, otherInstanceVersion));
    }

    [Fact]
    public async Task MissingAndAlreadyAttachedVersions_RejectWithoutChangingCurrentVersion()
    {
        DataRepositoryMock repository = new();
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string attachedVersion = await AllocateBlobVersion(repository, instanceGuid, dataElementId);
        await repository.Create(CreateDataElement(instanceGuid, dataElementId, attachedVersion));

        string missingVersion = BlobVersionId.Encode(Guid.NewGuid());
        RepositoryException missingException = await Assert.ThrowsAsync<RepositoryException>(() =>
            AttachBlobVersion(repository, instanceGuid, dataElementId, missingVersion)
        );
        RepositoryException attachedException = await Assert.ThrowsAsync<RepositoryException>(() =>
            AttachBlobVersion(repository, instanceGuid, dataElementId, attachedVersion)
        );

        Assert.Equal(HttpStatusCode.Conflict, missingException.StatusCodeSuggestion);
        Assert.Equal(
            $"Blob version was not available for data element {dataElementId}.",
            missingException.Message
        );
        Assert.Equal(HttpStatusCode.Conflict, attachedException.StatusCodeSuggestion);
        Assert.Equal(
            attachedVersion,
            (await repository.Read(instanceGuid, dataElementId)).BlobVersionId
        );
        Assert.Equal(
            [attachedVersion],
            Assert.Single(await repository.ReadBlobVersions(dataElementId)).BlobVersionIds
        );
    }

    [Fact]
    public async Task CurrentVersion_UsesAttachmentOrderWhileHistoryUsesAllocationOrder()
    {
        DataRepositoryMock repository = new();
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string firstAllocatedVersion = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            "first/app",
            "first",
            1
        );
        string secondAllocatedVersion = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            "second/app",
            "second",
            2
        );

        await repository.Create(
            CreateDataElement(instanceGuid, dataElementId, secondAllocatedVersion)
        );
        DataElementInternal updatedElement = await AttachBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            firstAllocatedVersion
        );

        Assert.Equal(firstAllocatedVersion, updatedElement.BlobVersionId);
        Assert.Equal(
            firstAllocatedVersion,
            (await repository.Read(instanceGuid, dataElementId)).BlobVersionId
        );
        Assert.Collection(
            await repository.ReadBlobVersions(dataElementId),
            firstContext => Assert.Equal([firstAllocatedVersion], firstContext.BlobVersionIds),
            secondContext => Assert.Equal([secondAllocatedVersion], secondContext.BlobVersionIds)
        );

        DataElementInternal nullUpdate = await AttachBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            null!
        );
        DataElementInternal emptyUpdate = await AttachBlobVersion(
            repository,
            instanceGuid,
            dataElementId,
            string.Empty
        );

        Assert.Equal(firstAllocatedVersion, nullUpdate.BlobVersionId);
        Assert.Equal(firstAllocatedVersion, emptyUpdate.BlobVersionId);
    }

    [Fact]
    public async Task DuplicateCreate_LeavesRequestedVersionDetachedAndCurrentUnchanged()
    {
        DataRepositoryMock repository = new();
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string currentVersion = await AllocateBlobVersion(repository, instanceGuid, dataElementId);
        string duplicateRequestedVersion = await AllocateBlobVersion(
            repository,
            instanceGuid,
            dataElementId
        );
        await repository.Create(CreateDataElement(instanceGuid, dataElementId, currentVersion));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.Create(
                CreateDataElement(instanceGuid, dataElementId, duplicateRequestedVersion)
            )
        );

        Assert.True(await repository.DeleteBlobVersion(dataElementId, duplicateRequestedVersion));
        Assert.Equal(
            currentVersion,
            (await repository.Read(instanceGuid, dataElementId)).BlobVersionId
        );
        Assert.Equal(
            [currentVersion],
            Assert.Single(await repository.ReadBlobVersions(dataElementId)).BlobVersionIds
        );
    }

    [Fact]
    public async Task UpdateValidationAndMalformedVersionFailures_AreAtomic()
    {
        DataRepositoryMock repository = new();
        Guid instanceGuid = Guid.NewGuid();
        Guid dataElementId = Guid.NewGuid();
        string currentVersion = await AllocateBlobVersion(repository, instanceGuid, dataElementId);
        string detachedVersion = await AllocateBlobVersion(repository, instanceGuid, dataElementId);
        await repository.Create(CreateDataElement(instanceGuid, dataElementId, currentVersion));

        await Assert.ThrowsAsync<InvalidCastException>(() =>
            repository.Update(
                instanceGuid,
                dataElementId,
                new Dictionary<string, object>
                {
                    ["/currentBlobVersion"] = detachedVersion,
                    ["/locked"] = "not-a-boolean",
                }
            )
        );
        RepositoryException malformedException = await Assert.ThrowsAsync<RepositoryException>(() =>
            AttachBlobVersion(repository, instanceGuid, dataElementId, "not-a-blob-version-id")
        );

        Assert.Equal(HttpStatusCode.BadRequest, malformedException.StatusCodeSuggestion);
        Assert.IsType<FormatException>(malformedException.InnerException);
        Assert.True(await repository.DeleteBlobVersion(dataElementId, detachedVersion));
        DataElementInternal unchangedElement = await repository.Read(instanceGuid, dataElementId);
        Assert.Equal(currentVersion, unchangedElement.BlobVersionId);
        Assert.False(unchangedElement.Locked);
    }

    private static DataElementInternal CreateDataElement(
        Guid instanceGuid,
        Guid dataElementId,
        string blobVersionId
    ) =>
        new()
        {
            Id = dataElementId.ToString(),
            InstanceGuid = instanceGuid.ToString(),
            BlobVersionId = blobVersionId,
        };

    private static async Task<DataElementInternal> AttachBlobVersion(
        DataRepositoryMock repository,
        Guid instanceGuid,
        Guid dataElementId,
        string blobVersionId
    ) =>
        await repository.Update(
            instanceGuid,
            dataElementId,
            new Dictionary<string, object> { ["/currentBlobVersion"] = blobVersionId }
        );

    private static Task<string> AllocateBlobVersion(
        DataRepositoryMock repository,
        Guid instanceGuid,
        Guid dataElementId,
        string appId = "ttd/app",
        string blobStorageOrg = "ttd",
        int? storageAccountNumber = 42
    ) =>
        repository.CreateBlobVersionId(
            instanceGuid,
            dataElementId,
            appId,
            blobStorageOrg,
            storageAccountNumber
        );
}
