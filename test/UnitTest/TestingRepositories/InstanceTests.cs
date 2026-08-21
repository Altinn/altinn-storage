#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class InstanceTests : IClassFixture<InstanceFixture>
{
    public enum ExpectedVersionKind
    {
        Instance,
        ProcessState,
    }

    public enum InstanceUpdateShape
    {
        Status,
        Substatus,
        PresentationTexts,
        DataValues,
        CompleteConfirmations,
        Process,
        ProcessAndStatus,
    }

    private readonly InstanceFixture _instanceFixture;

    public InstanceTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;

        string sql =
            "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    /// <summary>
    /// Test create
    /// </summary>
    [Fact]
    public async Task Instance_Create_Ok()
    {
        // Arrange
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        Guid expectedStorageId = input.Id;
        input.InternalId = 42;
        input.Versions = new StorageVersions(9, 8);

        // Act
        InstanceInternal newInstance = await _instanceFixture.InstanceRepo.Create(
            input,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool? confirmed = await PostgresUtil.RunQuery<bool?>(sql);
        Assert.Equal(1, count);
        Assert.Same(input, newInstance);
        Assert.Equal(expectedStorageId, newInstance.Id);
        Assert.Empty(newInstance.Data);
        Assert.Equal(new StorageVersions(1, 1), newInstance.Versions);
        Assert.Equal(0, newInstance.InternalId);
        Assert.Equal(0, newInstance.LastChanged.Value.Ticks % 10);
        Assert.Equal(false, confirmed);

        InstanceInternal persistedInstance = await _instanceFixture.InstanceRepo.GetOne(
            expectedStorageId,
            false,
            CancellationToken.None
        );
        Assert.NotSame(input, persistedInstance);
        Assert.Equal(expectedStorageId, persistedInstance.Id);
    }

    [Theory]
    [InlineData(null, "<absent>")]
    [InlineData(ProcessStatus.Idle, "\"idle\"")]
    [InlineData(ProcessStatus.Processing, "\"processing\"")]
    public async Task Instance_Create_PreservesProcessStatusPayload(
        ProcessStatus? suppliedStatus,
        string expectedStoredRepresentation
    )
    {
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        input.Id = Guid.NewGuid();
        input.Process = new ProcessState
        {
            Status = suppliedStatus,
            StartEvent = "creation-start",
            EndEvent = "creation-end",
            CurrentTask = new ProcessElementInfo
            {
                ElementId = "Task_Creation",
                AltinnTaskType = "data",
            },
        };

        InstanceInternal created = await _instanceFixture.InstanceRepo.Create(
            input,
            CancellationToken.None
        );
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            created.Id,
            false,
            CancellationToken.None
        );

        Assert.Same(input, created);
        Assert.Equal(suppliedStatus, created.Process.Status);
        Assert.Equal(suppliedStatus, persisted.Process.Status);
        Assert.Equal("creation-start", created.Process.StartEvent);
        Assert.Equal("creation-end", persisted.Process.EndEvent);
        Assert.Equal("Task_Creation", created.Process.CurrentTask.ElementId);
        Assert.Equal("Task_Creation", persisted.Process.CurrentTask.ElementId);
        Assert.Equal(
            expectedStoredRepresentation,
            await ReadStoredProcessStatusRepresentation(created.Id)
        );
    }

    [Fact]
    public async Task Instance_Create_GeneratesIdAndHydratesInternalStateOnlyOnRead()
    {
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        input.Id = Guid.Empty;

        InstanceInternal created = await _instanceFixture.InstanceRepo.Create(
            input,
            CancellationToken.None
        );

        Guid generatedId = created.Id;
        Assert.NotEqual(Guid.Empty, generatedId);
        Assert.Equal(0, created.InternalId);
        Assert.Equal(new StorageVersions(1, 1), created.Versions);

        InstanceInternal read = await _instanceFixture.InstanceRepo.GetOne(
            generatedId,
            false,
            CancellationToken.None
        );
        Assert.True(read.InternalId > 0);
        Assert.Equal(new StorageVersions(1, 1), read.Versions);
    }

    /// <summary>
    /// Test update task
    /// </summary>
    [Fact]
    public async Task Instance_Update_Task_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Process.CurrentTask.Name = "Before update";
        newInstance.Process.StartEvent = "s1";
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        StorageVersions versionsBefore = newInstance.Versions;
        newInstance.Process.CurrentTask.ElementId = "Task_2";
        newInstance.Process.CurrentTask.Name = "After update";
        newInstance.Process.StartEvent = null;
        newInstance.Process.EndEvent = "e1";
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.Process));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and taskid = 'Task_2'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal("Task_2", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal(
            newInstance.Process.CurrentTask.Name,
            updatedInstance.Process.CurrentTask.Name
        );
        Assert.Equal("After update", updatedInstance.Process.CurrentTask.Name);
        Assert.Equal("e1", newInstance.Process.EndEvent);
        Assert.Null(newInstance.Process.StartEvent);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(
            new StorageVersions(
                versionsBefore.InstanceVersion + 1,
                versionsBefore.ProcessStateVersion + 1
            ),
            updatedInstance.Versions
        );
        Assert.Equal("<absent>", await ReadStoredProcessStatusRepresentation(updatedInstance.Id));
    }

    [Fact]
    public async Task Instance_Update_PreservesCallerDomainDataList()
    {
        Instance instance = await CreateApiInstance(
            TestData.Instance_1_1.Clone(),
            CancellationToken.None
        );
        DataElement apiDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instance.Id.Split('/').Last(),
        };
        instance.Data = [apiDataElement];
        instance.LastChanged = DateTime.UtcNow;
        InstanceInternal input = InstanceInternalTestFactory.Create(
            instance,
            [apiDataElement.FromApiModel()],
            InternalId: 0
        );

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            input,
            [nameof(instance.LastChanged), nameof(instance.Process)],
            cancellationToken: CancellationToken.None
        );

        Assert.Same(input.Data, result.Data);
        Assert.Same(input.Data[0], Assert.Single(result.Data));
    }

    [Fact]
    public async Task Instance_UpdateReadStatus_PreservesCallerDomainDataList()
    {
        Instance instance = await CreateApiInstance(
            TestData.Instance_1_1.Clone(),
            CancellationToken.None
        );
        DataElement apiDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instance.Id.Split('/').Last(),
        };
        instance.Data = [apiDataElement];
        instance.Status.ReadStatus = ReadStatus.Unread;
        InstanceInternal input = InstanceInternalTestFactory.Create(
            instance,
            [apiDataElement.FromApiModel("input-blob-version")],
            InternalId: 0
        );

        InstanceInternal result = await _instanceFixture.InstanceRepo.UpdateReadStatus(
            input,
            CancellationToken.None
        );

        Assert.Same(input.Data, result.Data);
        Assert.Same(input.Data[0], Assert.Single(result.Data));
    }

    [Theory]
    [InlineData(ExpectedVersionKind.Instance)]
    [InlineData(ExpectedVersionKind.ProcessState)]
    public async Task Instance_Update_MismatchedExpectedVersion_ReportsCurrentVersions(
        ExpectedVersionKind versionKind
    )
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        instance.LastChanged = DateTime.UtcNow;

        StorageVersionMismatchException exception;
        if (versionKind == ExpectedVersionKind.Instance)
        {
            exception = await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                _instanceFixture.InstanceRepo.Update(
                    instance,
                    [nameof(instance.LastChanged)],
                    cancellationToken: CancellationToken.None,
                    expectedInstanceVersion: instance.Versions.InstanceVersion + 1
                )
            );
        }
        else
        {
            exception = await Assert.ThrowsAsync<ProcessStateVersionMismatchException>(() =>
                _instanceFixture.InstanceRepo.Update(
                    instance,
                    [nameof(instance.LastChanged)],
                    cancellationToken: CancellationToken.None,
                    expectedProcessStateVersion: instance.Versions.ProcessStateVersion + 1
                )
            );
        }

        Assert.Equal(instance.Versions.InstanceVersion, exception.CurrentInstanceVersion);
        Assert.Equal(instance.Versions.ProcessStateVersion, exception.CurrentProcessStateVersion);
    }

    [Theory]
    [InlineData(InstanceUpdateShape.Status, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.Substatus, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.PresentationTexts, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.DataValues, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.CompleteConfirmations, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.Process, ProcessStatus.Processing)]
    [InlineData(InstanceUpdateShape.ProcessAndStatus, ProcessStatus.Processing)]
    public async Task Instance_Update_NonIdleProcessStatus_ConflictsWithoutMutationOrVersionBump(
        InstanceUpdateShape updateShape,
        ProcessStatus currentProcessStatus
    )
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        await SetStoredProcessStatus(instanceGuid, currentProcessStatus);
        instance = await _instanceFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        List<string> updateProperties = PrepareInstanceUpdate(instance, updateShape);
        string storedBefore = await ReadStoredInstanceJson(instanceGuid);
        StorageVersions versionsBefore = await ReadStoredVersions(instanceGuid);

        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                _instanceFixture.InstanceRepo.Update(
                    instance,
                    updateProperties,
                    cancellationToken: CancellationToken.None
                )
            );

        Assert.Equal(currentProcessStatus, exception.CurrentProcessStatus);
        Assert.Equal(storedBefore, await ReadStoredInstanceJson(instanceGuid));
        Assert.Equal(versionsBefore, await ReadStoredVersions(instanceGuid));
    }

    [Theory]
    [InlineData("\"IDLE\"")]
    [InlineData("\" idle \"")]
    public async Task Instance_Update_StoredProcessStatusInNonCanonicalCasing_ConflictsWithoutMutationOrVersionBump(
        string storedStatusJson
    ) =>
        await AssertStoredProcessStatusBlocksUpdate<ProcessStatusConflictException>(
            storedStatusJson
        );

    [Theory]
    [InlineData("\"future-status\"")]
    [InlineData("99")]
    public async Task Instance_Update_UndeclaredStoredProcessStatus_FailsClosedWithoutMutationOrVersionBump(
        string storedStatusJson
    ) => await AssertStoredProcessStatusBlocksUpdate<UnreachableException>(storedStatusJson);

    private async Task AssertStoredProcessStatusBlocksUpdate<TException>(string storedStatusJson)
        where TException : Exception
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        await SetStoredProcessStatusRepresentation(instanceGuid, storedStatusJson);
        instance.LastChanged = DateTime.UtcNow;
        string storedBefore = await ReadStoredInstanceJson(instanceGuid);
        StorageVersions versionsBefore = await ReadStoredVersions(instanceGuid);

        await Assert.ThrowsAsync<TException>(() =>
            _instanceFixture.InstanceRepo.Update(
                instance,
                [nameof(instance.LastChanged)],
                cancellationToken: CancellationToken.None
            )
        );

        Assert.Equal(storedBefore, await ReadStoredInstanceJson(instanceGuid));
        Assert.Equal(versionsBefore, await ReadStoredVersions(instanceGuid));
    }

    [Fact]
    public async Task Instance_Update_StaleInstanceVersionWinsBeforeProcessStatusConflict()
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        instance.LastChanged = DateTime.UtcNow;

        InstanceVersionMismatchException exception =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                _instanceFixture.InstanceRepo.Update(
                    instance,
                    [nameof(instance.LastChanged)],
                    cancellationToken: CancellationToken.None,
                    expectedInstanceVersion: instance.Versions.InstanceVersion + 1
                )
            );

        Assert.Equal(
            instance.Versions,
            new StorageVersions(
                exception.CurrentInstanceVersion,
                exception.CurrentProcessStateVersion
            )
        );
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Theory]
    [InlineData(null, "<absent>")]
    [InlineData(ProcessStatus.Idle, "\"idle\"")]
    public async Task Instance_Update_Process_PersistsStatusPayload(
        ProcessStatus? suppliedStatus,
        string expectedStoredRepresentation
    )
    {
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Process.Status = ProcessStatus.Idle;
        instance = await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        await SetStoredProcessStatus(instance.Id, ProcessStatus.Idle);
        instance.Process = new ProcessState
        {
            Status = suppliedStatus,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_Normalized" },
        };
        instance.LastChanged = DateTime.UtcNow;

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            instance,
            [nameof(instance.Process), nameof(instance.LastChanged)],
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(suppliedStatus, result.Process.Status);
        Assert.Equal(
            expectedStoredRepresentation,
            await ReadStoredProcessStatusRepresentation(instance.Id)
        );
    }

    [Fact]
    public async Task Instance_Update_IdleToProcessingWithoutVersionPreconditions_PersistsAndBumpsBothVersions()
    {
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Process.Status = ProcessStatus.Idle;
        instance = await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        StorageVersions previousVersions = instance.Versions;
        instance.Process = new ProcessState
        {
            Status = ProcessStatus.Processing,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_Processing" },
        };
        instance.LastChanged = DateTime.UtcNow;

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            instance,
            [nameof(instance.Process), nameof(instance.LastChanged)],
            cancellationToken: CancellationToken.None
        );
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            instance.Id,
            false,
            CancellationToken.None
        );

        StorageVersions expectedVersions = new(
            previousVersions.InstanceVersion + 1,
            previousVersions.ProcessStateVersion + 1
        );
        Assert.Equal(ProcessStatus.Processing, result.Process.Status);
        Assert.Equal(expectedVersions, result.Versions);
        Assert.Equal(ProcessStatus.Processing, persisted.Process.Status);
        Assert.Equal(expectedVersions, persisted.Versions);
        Assert.Equal("\"processing\"", await ReadStoredProcessStatusRepresentation(instance.Id));
    }

    [Fact]
    public async Task Instance_Update_DataValuesAndProcess_DoesNotApplyProcessThroughTopLevelProperties()
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        StorageVersions versionsBefore = instance.Versions;
        string originalTaskId = instance.Process.CurrentTask.ElementId;
        instance.DataValues["combined-update"] = "applied";
        instance.Process = new ProcessState
        {
            Status = ProcessStatus.Processing,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_Smuggled" },
        };
        instance.LastChanged = DateTime.UtcNow;

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            instance,
            [nameof(instance.DataValues), nameof(instance.Process), nameof(instance.LastChanged)],
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("applied", result.DataValues["combined-update"]);
        Assert.Equal(originalTaskId, result.Process.CurrentTask.ElementId);
        Assert.Null(result.Process.Status);
        Assert.Equal(
            new StorageVersions(
                versionsBefore.InstanceVersion + 1,
                versionsBefore.ProcessStateVersion
            ),
            result.Versions
        );
        Assert.Equal(result.Versions, await ReadStoredVersions(instanceGuid));
        Assert.Equal("<absent>", await ReadStoredProcessStatusRepresentation(instanceGuid));
    }

    [Theory]
    [InlineData("status-absent", "<absent>")]
    [InlineData("status-null", "null")]
    [InlineData("process-absent", "<absent>")]
    [InlineData("process-null", "<absent>")]
    [InlineData("process-string", "<absent>")]
    public async Task Instance_Update_Process_ReplacesPersistedStatusRepresentation(
        string persistedRepresentation,
        string _
    )
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        await SetStoredProcessRepresentation(instanceGuid, persistedRepresentation);
        instance.Process = new ProcessState
        {
            Status = ProcessStatus.Processing,
            CurrentTask = new ProcessElementInfo { ElementId = "Task_Representation" },
        };

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            instance,
            [nameof(instance.Process)],
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("Task_Representation", result.Process.CurrentTask.ElementId);
        Assert.Equal("\"processing\"", await ReadStoredProcessStatusRepresentation(instanceGuid));
    }

    [Fact]
    public async Task Instance_UpdateReadStatus_ProcessingStatus_RemainsExemptWithoutVersionBump()
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        instance = await _instanceFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        StorageVersions versionsBefore = instance.Versions;
        instance.Status.ReadStatus = ReadStatus.Read;

        InstanceInternal result = await _instanceFixture.InstanceRepo.UpdateReadStatus(
            instance,
            CancellationToken.None
        );

        Assert.Equal(ReadStatus.Read, result.Status.ReadStatus);
        Assert.Equal(versionsBefore, result.Versions);
        Assert.Equal(versionsBefore, await ReadStoredVersions(instanceGuid));
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task Instance_UpdateReadStatus_StaleStatusSnapshot_KeepsOtherStatusFields()
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        instance = await _instanceFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        DateTime archived = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        DateTime softDeleted = new(2026, 8, 2, 10, 5, 0, DateTimeKind.Utc);
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status}}', instance -> 'Status' || '{{\"IsArchived\": true, \"Archived\": \"{archived:o}\", \"IsSoftDeleted\": true, \"SoftDeleted\": \"{softDeleted:o}\", \"Substatus\": {{\"Label\": \"sent-to-signing\", \"Description\": \"waiting\"}}}}'::jsonb) where alternateid = '{instanceGuid}'"
        );
        instance.Status.ReadStatus = ReadStatus.Unread;

        InstanceInternal result = await _instanceFixture.InstanceRepo.UpdateReadStatus(
            instance,
            CancellationToken.None
        );

        Assert.Equal(ReadStatus.Unread, result.Status.ReadStatus);
        Assert.True(result.Status.IsArchived);
        Assert.Equal(archived, result.Status.Archived);
        Assert.True(result.Status.IsSoftDeleted);
        Assert.Equal(softDeleted, result.Status.SoftDeleted);
        Assert.Equal("sent-to-signing", result.Status.Substatus.Label);
        Assert.Equal("waiting", result.Status.Substatus.Description);
    }

    [Theory]
    [InlineData("status-null")]
    [InlineData("status-absent")]
    public async Task Instance_UpdateReadStatus_StatusNotAnObject_WritesReadStatusOnly(
        string representation
    )
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        string instanceUpdate = representation switch
        {
            "status-null" => "jsonb_set(instance, '{Status}', 'null'::jsonb)",
            "status-absent" => "instance - 'Status'",
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                "Unknown status representation."
            ),
        };
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = {instanceUpdate} where alternateid = '{instanceGuid}'"
        );
        instance.Status = new InstanceStatus { ReadStatus = ReadStatus.Read };

        InstanceInternal result = await _instanceFixture.InstanceRepo.UpdateReadStatus(
            instance,
            CancellationToken.None
        );

        Assert.Equal(ReadStatus.Read, result.Status.ReadStatus);
        Assert.Equal(
            "{\"ReadStatus\": 1}",
            await PostgresUtil.RunQuery<string>(
                $"select (instance -> 'Status')::text from storage.instances where alternateid = '{instanceGuid}'"
            )
        );
    }

    [Fact]
    public async Task Instance_Update_RacingProcessingTransition_SerializesAndDoesNotMutate()
    {
        InstanceInternal instance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = instance.Id;
        string originalLastChangedBy = instance.LastChangedBy;
        instance.LastChanged = DateTime.UtcNow;
        instance.LastChangedBy = "racing-instance-update";
        StorageVersions versionsBefore = instance.Versions;

        await using NpgsqlConnection gateConnection =
            await _instanceFixture.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction gateTransaction =
            await gateConnection.BeginTransactionAsync();
        await using (
            NpgsqlCommand lockCommand = new(
                "select 1 from storage.instances where alternateid = $1 for update",
                gateConnection,
                gateTransaction
            )
        )
        {
            lockCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            Assert.Equal(1, Convert.ToInt32(await lockCommand.ExecuteScalarAsync()));

            Task<InstanceInternal> updateTask = _instanceFixture.InstanceRepo.Update(
                instance,
                [nameof(instance.LastChanged), nameof(instance.LastChangedBy)],
                cancellationToken: CancellationToken.None
            );

            try
            {
                await WaitForBlockedDatabaseCalls("storage.updateinstance_v4", expectedCount: 1);
            }
            catch
            {
                await gateTransaction.RollbackAsync();
                try
                {
                    await updateTask;
                }
                catch
                {
                    // Observe the task before propagating the synchronization failure.
                }

                throw;
            }

            await using NpgsqlCommand transitionCommand = new(
                """
                update storage.instances
                set instance = jsonb_set(
                        instance,
                        '{Process}',
                        (case
                            when jsonb_typeof(instance -> 'Process') = 'object'
                            then instance -> 'Process'
                            else '{}'::jsonb
                        end) || jsonb_build_object('Status', 'processing')
                    ),
                    instance_version = instance_version + 1,
                    process_state_version = process_state_version + 1
                where alternateid = $1
                """,
                gateConnection,
                gateTransaction
            );
            transitionCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            Assert.Equal(1, await transitionCommand.ExecuteNonQueryAsync());
            await gateTransaction.CommitAsync();

            ProcessStatusConflictException exception =
                await Assert.ThrowsAsync<ProcessStatusConflictException>(() => updateTask);

            Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
            InstanceInternal stored = await _instanceFixture.InstanceRepo.GetOne(
                instanceGuid,
                false,
                CancellationToken.None
            );
            Assert.Equal(originalLastChangedBy, stored.LastChangedBy);
            Assert.Equal(
                new StorageVersions(
                    versionsBefore.InstanceVersion + 1,
                    versionsBefore.ProcessStateVersion + 1
                ),
                stored.Versions
            );
        }
    }

    /// <summary>
    /// Test update with returned not_found result
    /// </summary>
    [Fact]
    public async Task Instance_Update_NotFoundResult_ThrowsNotFound()
    {
        // Arrange
        Instance newInstance = TestData.Instance_1_1.Clone();
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties =
        [
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
            nameof(newInstance.Process),
        ];

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            _instanceFixture.InstanceRepo.Update(
                InstanceInternalTestFactory.Create(newInstance, [], InternalId: 0),
                updateProperties,
                cancellationToken: CancellationToken.None
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
    }

    /// <summary>
    /// Test update read status with returned not_found result
    /// </summary>
    [Fact]
    public async Task Instance_UpdateReadStatus_NotFoundResult_ThrowsNotFound()
    {
        // Arrange
        Instance newInstance = TestData.Instance_1_1.Clone();
        newInstance.Status.ReadStatus = ReadStatus.Unread;

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            _instanceFixture.InstanceRepo.UpdateReadStatus(
                InstanceInternalTestFactory.Create(newInstance, [], InternalId: 0),
                CancellationToken.None
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
    }

    /// <summary>
    /// Test update status
    /// </summary>
    [Fact]
    public async Task Instance_Update_Status_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.IsArchived = true;
        newInstance.Status.Substatus = new() { Description = "desc " };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.Status.IsSoftDeleted = true;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties =
        [
            nameof(newInstance.Status),
            nameof(newInstance.Status.IsSoftDeleted),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(newInstance.Status.IsArchived, updatedInstance.Status.IsArchived);
        Assert.Equal(newInstance.Status.IsSoftDeleted, updatedInstance.Status.IsSoftDeleted);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(
            newInstance.Status.Substatus.Description,
            updatedInstance.Status.Substatus.Description
        );
    }

    /// <summary>
    /// Test update substatus
    /// </summary>
    [Fact]
    public async Task Instance_Update_Substatus_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.IsArchived = true;
        newInstance.Status.Substatus = new() { Description = "substatustest-desc" };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Status.Substatus = new() { Label = "substatustest-label" };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.IsArchived = false;

        List<string> updateProperties =
        [
            nameof(newInstance.Status.Substatus),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal("substatustest-label", updatedInstance.Status.Substatus.Label);
        Assert.Null(updatedInstance.Status.Substatus.Description);
        Assert.True(updatedInstance.Status.IsArchived);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update presentationtexts
    /// </summary>
    [Fact]
    public async Task Instance_Update_PresentationTexts_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "k1", "v1" }, { "k2", "v2" } };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.PresentationTexts = new() { { "k2", null }, { "k3", "v3" } };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.PresentationTexts));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.PresentationTexts.Count);
        Assert.True(updatedInstance.PresentationTexts.ContainsKey("k1"));
        Assert.True(updatedInstance.PresentationTexts.ContainsKey("k3"));
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update process
    /// </summary>
    [Fact]
    public async Task Instance_Update_Process_And_Status_Ok()
    {
        // Arrange
        DateTime unchangedSofteDeleted = DateTime.UtcNow.AddYears(-2);
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.SoftDeleted = unchangedSofteDeleted;
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process = new()
        {
            CurrentTask = new() { AltinnTaskType = "Task_3" },
            Ended = DateTime.Parse("2023-12-24"),
        };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.HardDeleted = DateTime.UtcNow;
        newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

        List<string> updateProperties =
        [
            nameof(newInstance.Process),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
            nameof(newInstance.Status),
            nameof(newInstance.Status.HardDeleted),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(
            newInstance.Process.CurrentTask.AltinnTaskType,
            updatedInstance.Process.CurrentTask.AltinnTaskType
        );
        Assert.Equal(newInstance.Process.Ended, updatedInstance.Process.Ended);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(newInstance.Status.HardDeleted, updatedInstance.Status.HardDeleted);
        Assert.Equal(unchangedSofteDeleted, updatedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test update process without updating status
    /// </summary>
    [Fact]
    public async Task Instance_Update_Process_And_No_Status_Ok()
    {
        // Arrange
        DateTime unchangedSofteDeleted = DateTime.UtcNow.AddYears(-2);
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.SoftDeleted = unchangedSofteDeleted;
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process = new()
        {
            CurrentTask = new() { AltinnTaskType = "Task_3" },
            Ended = DateTime.Parse("2023-12-24"),
        };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

        List<string> updateProperties =
        [
            nameof(newInstance.Process),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(
            newInstance.Process.CurrentTask.AltinnTaskType,
            updatedInstance.Process.CurrentTask.AltinnTaskType
        );
        Assert.Equal(newInstance.Process.Ended, updatedInstance.Process.Ended);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(unchangedSofteDeleted, updatedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test update data values
    /// </summary>
    [Fact]
    public async Task Instance_Update_DataValues_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "k1", "v1" }, { "k2", "v2" } };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.DataValues = new() { { "k2", null }, { "k3", "v3" } };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.DataValues));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.DataValues.Count);
        Assert.True(updatedInstance.DataValues.ContainsKey("k1"));
        Assert.True(updatedInstance.DataValues.ContainsKey("k3"));
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update CompleteConfirmations
    /// </summary>
    [Fact]
    public async Task Instance_Update_CompleteConfirmations_PrimaryOrg_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-1),
                StakeholderId = "TTD",
            },
        ];
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-2),
                StakeholderId = "s2",
            },
        ];
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.CompleteConfirmations));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool? confirmed = await PostgresUtil.RunQuery<bool?>(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.CompleteConfirmations.Count);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.True(confirmed);
    }

    /// <summary>
    /// Test update CompleteConfirmations
    /// </summary>
    [Fact]
    public async Task Instance_Update_CompleteConfirmations_OtherOrg_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-1),
                StakeholderId = "s1",
            },
        ];
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-2),
                StakeholderId = "s2",
            },
        ];
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.CompleteConfirmations));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            cancellationToken: CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool confirmed = await PostgresUtil.RunQuery<bool>(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.CompleteConfirmations.Count);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.False(confirmed);
    }

    /// <summary>
    /// Test delete
    /// </summary>
    [Fact]
    public async Task Instance_Delete_Ok()
    {
        // Arrange
        InstanceInternal newInstance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        // Act
        bool deleted = await _instanceFixture.InstanceRepo.Delete(
            newInstance.Id,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
        Assert.True(deleted);
    }

    /// <summary>
    /// Test GetOne
    /// </summary>
    [Fact]
    public async Task Instance_GetOne_Ok()
    {
        // Arrange
        DataElementInternal data = TestDataUtil
            .GetDataElement("cdb627fd-c586-41f5-99db-bae38daa2b59")
            .FromApiModel();
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        string blobVersionId = await _instanceFixture.DataRepo.CreateBlobVersionId(
            data.InstanceGuid,
            data.Id,
            input.AppId,
            input.Org,
            null,
            CancellationToken.None
        );
        data.BlobVersionId = blobVersionId;
        data.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            input.AppId,
            data.InstanceGuid,
            blobVersionId
        );
        InstanceInternal instance = await InsertInstanceAndData(input, data);

        // Act
        InstanceInternal instanceNoData = await _instanceFixture.InstanceRepo.GetOne(
            instance.Id,
            false,
            CancellationToken.None
        );
        InstanceInternal instanceWithData = await _instanceFixture.InstanceRepo.GetOne(
            instance.Id,
            true,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(instanceNoData.Id, instance.Id);
        Assert.Equal(instanceWithData.Id, instance.Id);
        Assert.Empty(instanceNoData.Data);
        DataElementInternal hydrated = Assert.Single(instanceWithData.Data);
        Assert.Equal(blobVersionId, hydrated.BlobVersionId);
        Assert.Equal(data.BlobStoragePath, hydrated.BlobStoragePath);
    }

    /// <summary>
    /// Test GetHardDeletedInstances
    /// </summary>
    [Fact]
    public async Task Instance_GetHardDeletedInstances_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            HardDelete(TestData.Instance_1_1.Clone().FromApiModel()),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            HardDelete(TestData.Instance_2_1.Clone().FromApiModel()),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_3_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        InstanceInternal freshHardDeleted = await _instanceFixture.InstanceRepo.Create(
            HardDelete(TestData.Instance_1_3.Clone().FromApiModel(), DateTime.UtcNow),
            CancellationToken.None
        );

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetHardDeletedInstances(
            CancellationToken.None
        );

        // Assert
        Assert.Equal(2, instances.Count);
        Assert.DoesNotContain(instances, i => i.Id == freshHardDeleted.Id);
        Assert.All(
            instances,
            instance =>
            {
                Assert.Null(instance.Data);
                Assert.Null(instance.Versions);
                Assert.Equal(0, instance.InternalId);
            }
        );
    }

    /// <summary>
    /// Test GetHardDeletedDataElements
    /// </summary>
    [Fact]
    public async Task Instance_GetHardDeletedDataElements_Ok()
    {
        // Arrange
        DataElementInternal data1 = TestDataUtil
            .GetDataElement("11f7c994-6681-47a1-9626-fcf6c27308a5")
            .FromApiModel();
        DataElementInternal data2 = TestDataUtil
            .GetDataElement("1336b773-4ae2-4bdf-9529-d71dfc1c8b43")
            .FromApiModel();
        DataElementInternal data3 = TestDataUtil
            .GetDataElement("24bfec2e-c4ce-4e82-8fa9-aa39da329fd5")
            .FromApiModel();
        data1.InstanceGuid = new Guid(TestData.Instance_1_1.Id.Split('/').Last());
        data2.InstanceGuid = new Guid(TestData.Instance_2_1.Id.Split('/').Last());
        data3.InstanceGuid = new Guid(TestData.Instance_3_1.Id.Split('/').Last());
        InstanceInternal instance1 = TestData.Instance_1_1.Clone().FromApiModel();
        string firstVersion = await _instanceFixture.DataRepo.CreateBlobVersionId(
            data1.InstanceGuid,
            data1.Id,
            instance1.AppId,
            instance1.Org,
            null,
            CancellationToken.None
        );
        string secondVersion = await _instanceFixture.DataRepo.CreateBlobVersionId(
            data1.InstanceGuid,
            data1.Id,
            instance1.AppId,
            instance1.Org,
            null,
            CancellationToken.None
        );
        data1.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance1.AppId,
            data1.InstanceGuid,
            secondVersion
        );
        data1.BlobVersionId = secondVersion;
        await InsertInstanceAndDataHardDelete(instance1, data1);
        Guid firstVersionUuid = BlobVersionId.Decode(firstVersion);
        await PostgresUtil.RunSql(
            $"update storage.dataelementblobversions set attached = true where id = '{firstVersionUuid}'"
        );
        await InsertInstanceAndDataHardDelete(TestData.Instance_2_1.Clone().FromApiModel(), data2);
        await InsertInstanceAndDataHardDelete(TestData.Instance_3_1.Clone().FromApiModel(), data3);

        // Act
        var dataElements3 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements(
            CancellationToken.None
        );
        await PostgresUtil.RunSql(
            $"update storage.dataelements set element = jsonb_set(element, '{{DeleteStatus,IsHardDeleted}}', 'false') where alternateid = '{data1.Id}'"
        );
        var dataElements2 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements(
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, dataElements3.Count);
        Assert.Equal(2, dataElements2.Count);
        DeletedDataElementInternal versionedElement = Assert.Single(
            dataElements3,
            element => element.DataElement.Id == data1.Id
        );
        BlobVersionReferencesInternal blobVersions = Assert.Single(versionedElement.BlobVersions);
        Assert.Equal(data1.InstanceGuid, blobVersions.InstanceGuid);
        Assert.Equal(instance1.AppId, blobVersions.AppId);
        Assert.Equal(instance1.Org, blobVersions.BlobStorageOrg);
        Assert.Equal([firstVersion, secondVersion], blobVersions.BlobVersionIds);
    }

    [Fact]
    public async Task Instance_GetOrphanBlobVersionsForCleanup_Ok()
    {
        // Arrange
        Instance instance = await CreateApiInstance(
            TestData.Instance_1_1.Clone(),
            CancellationToken.None
        );
        Guid instanceGuid = Guid.Parse(instance.Id.Split('/').Last());
        string firstOldVersion = await CreateBlobVersionId(instanceGuid, Guid.NewGuid(), instance);
        string secondOldVersion = await CreateBlobVersionId(instanceGuid, Guid.NewGuid(), instance);
        Guid firstOldVersionUuid = BlobVersionId.Decode(firstOldVersion);
        Guid secondOldVersionUuid = BlobVersionId.Decode(secondOldVersion);
        await PostgresUtil.RunSql(
            $"update storage.dataelementblobversions set created = now() - interval '8 days' where id in ('{firstOldVersionUuid}', '{secondOldVersionUuid}')"
        );

        await _instanceFixture.DataRepo.CreateBlobVersionId(
            instanceGuid,
            Guid.NewGuid(),
            instance.AppId,
            instance.Org,
            null,
            CancellationToken.None
        );

        DataElement existingDataElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        Instance existingInstance = TestData.Instance_2_1.Clone();
        string existingVersion = await CreateBlobVersionId(
            Guid.Parse(existingDataElement.InstanceGuid),
            Guid.Parse(existingDataElement.Id),
            existingInstance
        );
        existingDataElement.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            existingInstance.AppId,
            new Guid(existingDataElement.InstanceGuid),
            existingVersion
        );
        Guid existingVersionUuid = BlobVersionId.Decode(existingVersion);
        await PostgresUtil.RunSql(
            $"update storage.dataelementblobversions set created = now() - interval '8 days' where id = '{existingVersionUuid}'"
        );
        await InsertInstanceAndData(existingInstance, existingDataElement, existingVersion);

        // Act
        List<BlobVersionReferencesInternal> orphanBlobVersions =
            await _instanceFixture.InstanceRepo.GetOrphanBlobVersionsForCleanup(
                CancellationToken.None
            );

        // Assert
        BlobVersionReferencesInternal orphanBlobVersion = Assert.Single(orphanBlobVersions);
        Assert.Equal(instanceGuid, orphanBlobVersion.InstanceGuid);
        Assert.Equal(instance.AppId, orphanBlobVersion.AppId);
        Assert.Equal(instance.Org, orphanBlobVersion.BlobStorageOrg);
        Assert.Equal(
            new[] { firstOldVersion, secondOldVersion }.OrderBy(version => version),
            orphanBlobVersion.BlobVersionIds.OrderBy(version => version)
        );
    }

    [Fact]
    public async Task Instance_GetBlobVersionsForInstance_Ok()
    {
        // Arrange
        DataElement dataElement = TestDataUtil.GetDataElement(
            "24bfec2e-c4ce-4e82-8fa9-aa39da329fd5"
        );
        Instance instance = TestData.Instance_1_1.Clone();
        string firstVersion = await CreateBlobVersionId(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            instance
        );
        string secondVersion = await CreateBlobVersionId(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            instance
        );
        dataElement.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            new Guid(dataElement.InstanceGuid),
            secondVersion
        );
        await InsertInstanceAndData(instance, dataElement, secondVersion);
        Guid firstVersionUuid = BlobVersionId.Decode(firstVersion);
        await PostgresUtil.RunSql(
            $"update storage.dataelementblobversions set attached = true where id = '{firstVersionUuid}'"
        );
        DataElement otherDataElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        otherDataElement.InstanceGuid = dataElement.InstanceGuid;
        string otherVersion = await CreateBlobVersionId(
            Guid.Parse(otherDataElement.InstanceGuid),
            Guid.Parse(otherDataElement.Id),
            instance
        );
        otherDataElement.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            new Guid(otherDataElement.InstanceGuid),
            otherVersion
        );
        InstanceInternal instanceInternal = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(dataElement.InstanceGuid),
            true,
            CancellationToken.None
        );
        await _instanceFixture.DataRepo.Create(
            otherDataElement.FromApiModel(otherVersion),
            instanceInternal.InternalId,
            cancellationToken: CancellationToken.None
        );

        // Act
        List<BlobVersionReferencesInternal> blobVersions =
            await _instanceFixture.InstanceRepo.GetBlobVersionsForInstance(
                Guid.Parse(dataElement.InstanceGuid),
                CancellationToken.None
            );

        // Assert
        Assert.Equal(2, blobVersions.Count);
        BlobVersionReferencesInternal blobVersion = Assert.Single(
            blobVersions,
            versions => versions.BlobVersionIds.Contains(firstVersion)
        );
        Assert.Equal(Guid.Parse(dataElement.InstanceGuid), blobVersion.InstanceGuid);
        Assert.Equal(instance.AppId, blobVersion.AppId);
        Assert.Equal(instance.Org, blobVersion.BlobStorageOrg);
        Assert.Equal([firstVersion, secondVersion], blobVersion.BlobVersionIds);
        BlobVersionReferencesInternal otherBlobVersion = Assert.Single(
            blobVersions,
            versions => versions.BlobVersionIds.Contains(otherVersion)
        );
        Assert.Equal([otherVersion], otherBlobVersion.BlobVersionIds);
    }

    /// <summary>
    /// Test GetInstancesFromQuery
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_FullyHydratesDomainState()
    {
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        Guid expectedStorageId = input.Id;
        await _instanceFixture.InstanceRepo.Create(input, CancellationToken.None);

        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            expectedStorageId,
            false,
            CancellationToken.None
        );
        DataElement firstInsertedElement = TestDataUtil.GetDataElement(
            "24bfec2e-c4ce-4e82-8fa9-aa39da329fd5"
        );
        firstInsertedElement.InstanceGuid = expectedStorageId.ToString();
        string firstBlobVersionId = await _instanceFixture.DataRepo.CreateBlobVersionId(
            expectedStorageId,
            Guid.Parse(firstInsertedElement.Id),
            input.AppId,
            input.Org,
            null,
            CancellationToken.None
        );
        firstInsertedElement.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            input.AppId,
            expectedStorageId,
            firstBlobVersionId
        );
        await _instanceFixture.DataRepo.Create(
            firstInsertedElement.FromApiModel(firstBlobVersionId),
            persisted.InternalId,
            cancellationToken: CancellationToken.None
        );

        DataElement secondInsertedElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        secondInsertedElement.InstanceGuid = expectedStorageId.ToString();
        string secondBlobVersionId = await _instanceFixture.DataRepo.CreateBlobVersionId(
            expectedStorageId,
            Guid.Parse(secondInsertedElement.Id),
            input.AppId,
            input.Org,
            null,
            CancellationToken.None
        );
        await _instanceFixture.DataRepo.Create(
            secondInsertedElement.FromApiModel(secondBlobVersionId),
            persisted.InternalId,
            cancellationToken: CancellationToken.None
        );
        Assert.NotEqual(firstBlobVersionId, secondBlobVersionId);
        await PostgresUtil.RunSql(
            $"update storage.instances set instance_version = 7, process_state_version = 3 where id = {persisted.InternalId}"
        );

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                AppId = input.AppId,
                IncludeDataElements = true,
            },
            CancellationToken.None
        );

        Assert.Null(result.Exception);
        InstanceInternal instance = Assert.Single(result.Instances);
        Assert.Equal(expectedStorageId, instance.Id);
        Assert.Equal(persisted.InternalId, instance.InternalId);
        Assert.NotEqual(0, instance.InternalId);
        Assert.Equal(new StorageVersions(7, 3), instance.Versions);
        Assert.Collection(
            instance.Data,
            element =>
            {
                Assert.Equal(firstInsertedElement.Id, element.Id.ToString());
                Assert.Equal(firstBlobVersionId, element.BlobVersionId);
                Assert.Equal(firstInsertedElement.BlobStoragePath, element.BlobStoragePath);
            },
            element =>
            {
                Assert.Equal(secondInsertedElement.Id, element.Id.ToString());
                Assert.Equal(secondBlobVersionId, element.BlobVersionId);
            }
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_PreservesOrderingFilteringAndContinuation()
    {
        Instance first = TestData.Instance_1_1.Clone();
        Instance second = TestData.Instance_1_2.Clone();
        Instance third = TestData.Instance_1_3.Clone();
        first.LastChanged = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        second.LastChanged = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        third.LastChanged = new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        await CreateApiInstance(first, CancellationToken.None);
        await CreateApiInstance(second, CancellationToken.None);
        await CreateApiInstance(third, CancellationToken.None);

        InstanceQueryParameters query = new()
        {
            Size = 1,
            SortBy = "asc:lastChanged",
            IncludeDataElements = false,
        };
        InstanceQueryResult firstPage = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            query,
            CancellationToken.None
        );
        query.ContinuationToken = firstPage.ContinuationToken;
        InstanceQueryResult secondPage = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            query,
            CancellationToken.None
        );

        Assert.Equal(first.Id.Split('/').Last(), Assert.Single(firstPage.Instances).Id.ToString());
        Assert.Equal(
            second.Id.Split('/').Last(),
            Assert.Single(secondPage.Instances).Id.ToString()
        );
        Assert.NotNull(firstPage.ContinuationToken);
        Assert.NotNull(secondPage.ContinuationToken);
        Assert.NotEqual(firstPage.ContinuationToken, secondPage.ContinuationToken);
        Assert.Empty(firstPage.Instances[0].Data);

        InstanceQueryResult filtered = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                InstanceOwnerPartyId = Convert.ToInt32(third.InstanceOwner.PartyId),
                IncludeDataElements = false,
            },
            CancellationToken.None
        );
        Assert.Equal(third.Id.Split('/').Last(), Assert.Single(filtered.Instances).Id.ToString());
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_PreservesDeleteStatusForConsumerFiltering()
    {
        Instance instance = TestData.Instance_1_1.Clone();
        await CreateApiInstance(instance, CancellationToken.None);
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        DataElement visibleElement = TestDataUtil.GetDataElement(
            "24bfec2e-c4ce-4e82-8fa9-aa39da329fd5"
        );
        visibleElement.InstanceGuid = persisted.Id.ToString();
        DataElement deletedElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        deletedElement.InstanceGuid = persisted.Id.ToString();
        deletedElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        await _instanceFixture.DataRepo.Create(
            visibleElement.FromApiModel(),
            persisted.InternalId,
            cancellationToken: CancellationToken.None
        );
        await _instanceFixture.DataRepo.Create(
            deletedElement.FromApiModel(),
            persisted.InternalId,
            cancellationToken: CancellationToken.None
        );

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters { Size = 100, IncludeDataElements = true },
            CancellationToken.None
        );

        List<DataElementInternal> elements = Assert.Single(result.Instances).Data;
        Assert.Equal(2, elements.Count);
        Assert.True(
            Assert
                .Single(elements, element => element.Id.ToString() == deletedElement.Id)
                .DeleteStatus.IsHardDeleted
        );
        Assert.Equal(
            visibleElement.Id,
            Assert
                .Single(elements, element => element.DeleteStatus?.IsHardDeleted != true)
                .Id.ToString()
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_ReturnsErrorResultOnCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters { Size = 100, IncludeDataElements = true },
            cancellation.Token
        );

        Assert.Empty(result.Instances);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_ReturnsErrorResultOnInvalidQuery()
    {
        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                ProcessEnded = ["true"],
                IncludeDataElements = true,
            },
            CancellationToken.None
        );

        Assert.Empty(result.Instances);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void InstanceQueryResult_IsNotAnApiWireModel()
    {
        string[] propertyNames =
        [
            .. typeof(InstanceQueryResult)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name),
        ];

        Assert.Equal(["ContinuationToken", "Exception", "Instances"], propertyNames);
        Assert.All(
            typeof(InstanceQueryResult).GetProperties(),
            property =>
                Assert.Empty(
                    property.GetCustomAttributes(
                        typeof(Newtonsoft.Json.JsonPropertyAttribute),
                        inherit: true
                    )
                )
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new() { Size = 100 };

        // Act
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        queryParams.InstanceOwnerPartyId = Convert.ToInt32(
            TestData.Instance_1_3.InstanceOwner.PartyId
        );
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, instances3.Instances.Count);
        Assert.Single(instances1.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with continuation token
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_Continuation_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );
        InstanceQueryParameters queryParams = new() { Size = 1, SortBy = "asc:" };

        // Act
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken1 = instances1.ContinuationToken;
        queryParams.ContinuationToken = contToken1;

        var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken2 = instances2.ContinuationToken;
        queryParams.ContinuationToken = contToken2;

        queryParams.Size = 2;
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken3 = instances3.ContinuationToken;

        // Assert
        Assert.Single(instances1.Instances);
        Assert.Single(instances2.Instances);
        Assert.Single(instances3.Instances);
        Assert.Null(contToken3);
        Assert.True(string.CompareOrdinal(contToken1, contToken2) < 0);
        Assert.Equal(
            instances1.Instances.FirstOrDefault().Id.ToString(),
            TestData.Instance_1_1.Id.Split('/').Last()
        );
        Assert.Equal(
            instances2.Instances.FirstOrDefault().Id.ToString(),
            TestData.Instance_1_2.Id.Split('/').Last()
        );
        Assert.Equal(
            instances3.Instances.FirstOrDefault().Id.ToString(),
            TestData.Instance_1_3.Id.Split('/').Last()
        );
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appId
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_AppId_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            AppId = "ttd/test-applikasjon-1",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test use of confirmed
    /// </summary>
    [Fact]
    public void Instance_Confirmed_IsSetCorrectly()
    {
        // Arrange
        InstanceQueryParameters queryParamsWithExcludeOwner = new()
        {
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        InstanceQueryParameters queryParamsWithExcludeOther = new()
        {
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        InstanceQueryParameters queryParamsWithoutExclude = new() { Org = "TTD" };

        // Act
        var sqlParamsWithExcludeOwner = queryParamsWithExcludeOwner.GeneratePostgreSQLParameters();
        var sqlParamsWithExcludeOther = queryParamsWithExcludeOther.GeneratePostgreSQLParameters();
        var sqlParamsWithoutExclude = queryParamsWithoutExclude.GeneratePostgreSQLParameters();

        // Assert
        Assert.False((bool?)sqlParamsWithExcludeOwner["_confirmed"]);
        Assert.False(sqlParamsWithExcludeOwner.ContainsKey("_excludeConfirmedBy"));

        Assert.False(sqlParamsWithExcludeOther.ContainsKey("_confirmed"));
        Assert.True(sqlParamsWithExcludeOther.ContainsKey("_excludeConfirmedBy"));

        Assert.False(sqlParamsWithoutExclude.ContainsKey("_confirmed"));
        Assert.False(sqlParamsWithoutExclude.ContainsKey("_excludeConfirmedBy"));
    }

    /// <summary>
    /// Test GetInstancesFromQuery with PresentationFields, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_NoMatchFromPresentationFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "nomatchj",
            AppIds = [],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with PresentationFields, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromPresentationFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "bing",
            AppIds = [],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A2ArchRef data value, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_DataValuesA2ArchRef_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "A2ArchRef", "123456" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None, 2);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            DataValuesA2ArchRef = "123456",
            MainVersionInclude = 2,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A2ArchRef data value, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_DataValuesA2ArchRef_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "A2ArchRef", "123456" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None, 2);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            DataValuesA2ArchRef = "654321",
            MainVersionInclude = 2,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A3 reference, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_A3Ref_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            A3Ref = "b7fe18ccff30",
            MainVersionInclude = 3,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A3 reference, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_A3Ref_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            A3Ref = "000000000000",
            MainVersionInclude = 3,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, primary org, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_PrimaryOrg_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "TTD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, primary org, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_PrimaryOrg_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "TTD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, other org, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_OtherOrg_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "SKD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, other org, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_OtherOrg_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "SKD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appIds, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromAppIds_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "nomatch",
            AppIds = ["ttd/test-applikasjon-1", "ttd/test-applikasjon-2"],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appIds and presentation fields, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromAppIdsAndPresFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance1 = TestData.Instance_1_1.Clone().FromApiModel();
        InstanceInternal newInstance2 = TestData.Instance_1_2.Clone().FromApiModel();
        newInstance1.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        newInstance2.AppId = "ttd/test-applikasjon-3";
        await _instanceFixture.InstanceRepo.Create(newInstance1, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance2, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "ing",
            AppIds = new List<string>()
            {
                "ttd/test-applikasjon-3",
                "ttd/test-applikasjon-2",
            }.ToArray(),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(2, instances.Instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with msgBoxInterval
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromMsgBoxInterval_Ok()
    {
        // Arrange
        await PrepareDateSearch();

        // Act
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2021", "2021"),
            CancellationToken.None
        );
        var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2022", "2022"),
            CancellationToken.None
        );
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2023", "2023"),
            CancellationToken.None
        );
        var instances4 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2024", "2024"),
            CancellationToken.None
        );
        var instances5 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2019", "2019"),
            CancellationToken.None
        );
        var instances6 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2021", "2024"),
            CancellationToken.None
        );

        // Assert
        Assert.Single(instances1.Instances);
        Assert.Single(instances2.Instances);
        Assert.Single(instances3.Instances);
        Assert.Single(instances4.Instances);
        Assert.Empty(instances5.Instances);
        Assert.Equal(4, instances6.Instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with bad date
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_InvalidDate()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ProcessEnded = ["true"],
            InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
        Assert.NotNull(instances.Exception);
    }

    /// <summary>
    /// Test GetInstancesFromQuery, IncludeDataElements query parameter
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_WithIncludeDataElementsAsQueryParam_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new() { Size = 100, IncludeDataElements = true };

        // Act
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        queryParams.InstanceOwnerPartyId = Convert.ToInt32(
            TestData.Instance_1_3.InstanceOwner.PartyId
        );
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, instances3.Instances.Count);
        Assert.Single(instances1.Instances);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with bad date
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_WithIncludeDataElementsAsQueryParam_InvalidDate()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ProcessEnded = ["true"],
            InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Empty(instances.Instances);
        Assert.NotNull(instances.Exception);
    }

    /// <summary>
    /// Test create instance with Email-based self identification
    /// </summary>
    [Fact]
    public async Task Instance_Create_WithEmail_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_Email.Clone().FromApiModel();

        // Act
        InstanceInternal createdInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{newInstance.Id}'";
        int count = await PostgresUtil.RunCountQuery(sql);

        Assert.Equal(1, count);
        Assert.Equal(newInstance.InstanceOwner.PartyId, createdInstance.InstanceOwner.PartyId);
        Assert.NotNull(createdInstance.InstanceOwner.Username);
        Assert.Equal("epost:test.user@example.com", createdInstance.InstanceOwner.Username);
        Assert.NotNull(createdInstance.InstanceOwner.ExternalIdentifier);
        Assert.Equal(
            "urn:altinn:person:idporten-email:test.user@example.com",
            createdInstance.InstanceOwner.ExternalIdentifier
        );
    }

    /// <summary>
    /// Test create instance with Username-based self identification (legacy)
    /// </summary>
    [Fact]
    public async Task Instance_Create_WithUsername_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_Username.Clone().FromApiModel();

        // Act
        InstanceInternal createdInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{newInstance.Id}'";
        int count = await PostgresUtil.RunCountQuery(sql);

        Assert.Equal(1, count);
        Assert.Equal(newInstance.InstanceOwner.PartyId, createdInstance.InstanceOwner.PartyId);
        Assert.NotNull(createdInstance.InstanceOwner.Username);
        Assert.Equal("legacy_username", createdInstance.InstanceOwner.Username);
    }

    private async Task<Instance> CreateApiInstance(
        Instance instance,
        CancellationToken cancellationToken
    ) =>
        (
            await _instanceFixture.InstanceRepo.Create(instance.FromApiModel(), cancellationToken)
        ).ToApiModel();

    private Task<string> CreateBlobVersionId(
        Guid instanceGuid,
        Guid dataElementId,
        Instance instance
    ) =>
        _instanceFixture.DataRepo.CreateBlobVersionId(
            instanceGuid,
            dataElementId,
            instance.AppId,
            instance.Org,
            null,
            CancellationToken.None
        );

    private async Task<InstanceInternal> InsertInstanceAndDataHardDelete(
        InstanceInternal instance,
        DataElementInternal dataelement
    )
    {
        dataelement.DeleteStatus = new()
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.Now.AddDays(-8).ToUniversalTime(),
        };
        instance.CompleteConfirmations = new()
        {
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.Now.AddDays(-8).ToUniversalTime(),
                StakeholderId = instance.Org,
            },
        };

        return await InsertInstanceAndData(instance, dataelement);
    }

    private async Task<InstanceInternal> InsertInstanceAndData(
        InstanceInternal instance,
        DataElementInternal dataelement
    )
    {
        instance = await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        long internalId = (
            await _instanceFixture.InstanceRepo.GetOne(instance.Id, true, CancellationToken.None)
        ).InternalId;
        await _instanceFixture.DataRepo.Create(dataelement, internalId);
        return instance;
    }

    private async Task<InstanceInternal> InsertInstanceAndData(
        Instance instance,
        DataElement dataelement,
        string blobVersionId
    )
    {
        InstanceInternal instanceInternal = instance.FromApiModel();
        instanceInternal.Id = new Guid(dataelement.InstanceGuid);
        return await InsertInstanceAndData(
            instanceInternal,
            dataelement.FromApiModel(blobVersionId)
        );
    }

    private static InstanceInternal HardDelete(
        InstanceInternal instance,
        DateTime? hardDeleted = null
    )
    {
        instance.Status.IsHardDeleted = true;
        instance.Status.HardDeleted = hardDeleted ?? DateTime.Now.AddDays(-8).ToUniversalTime();
        instance.CompleteConfirmations = new();
        return instance;
    }

    private static List<string> PrepareInstanceUpdate(
        InstanceInternal instance,
        InstanceUpdateShape updateShape
    )
    {
        instance.LastChanged = DateTime.UtcNow;
        instance.LastChangedBy = "blocked-instance-update";
        List<string> updateProperties =
        [
            nameof(instance.LastChanged),
            nameof(instance.LastChangedBy),
        ];

        switch (updateShape)
        {
            case InstanceUpdateShape.Status:
                instance.Status.IsSoftDeleted = true;
                instance.Status.SoftDeleted = DateTime.UtcNow;
                updateProperties.Add(nameof(instance.Status));
                updateProperties.Add(nameof(instance.Status.IsSoftDeleted));
                updateProperties.Add(nameof(instance.Status.SoftDeleted));
                break;
            case InstanceUpdateShape.Substatus:
                instance.Status.Substatus = new Substatus { Label = "blocked-substatus" };
                updateProperties.Add(nameof(instance.Status.Substatus));
                break;
            case InstanceUpdateShape.PresentationTexts:
                instance.PresentationTexts = new Dictionary<string, string>
                {
                    ["blocked-presentation"] = "value",
                };
                updateProperties.Add(nameof(instance.PresentationTexts));
                break;
            case InstanceUpdateShape.DataValues:
                instance.DataValues = new Dictionary<string, string>
                {
                    ["blocked-data-value"] = "value",
                };
                updateProperties.Add(nameof(instance.DataValues));
                break;
            case InstanceUpdateShape.CompleteConfirmations:
                instance.CompleteConfirmations =
                [
                    new CompleteConfirmation
                    {
                        StakeholderId = "blocked-stakeholder",
                        ConfirmedOn = DateTime.UtcNow,
                    },
                ];
                updateProperties.Add(nameof(instance.CompleteConfirmations));
                break;
            case InstanceUpdateShape.Process:
                instance.Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Blocked" },
                };
                updateProperties.Add(nameof(instance.Process));
                break;
            case InstanceUpdateShape.ProcessAndStatus:
                instance.Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Blocked" },
                };
                instance.Status.IsArchived = true;
                instance.Status.Archived = DateTime.UtcNow;
                updateProperties.Add(nameof(instance.Process));
                updateProperties.Add(nameof(instance.Status));
                updateProperties.Add(nameof(instance.Status.IsArchived));
                updateProperties.Add(nameof(instance.Status.Archived));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(updateShape), updateShape, null);
        }

        return updateProperties;
    }

    private static Task SetStoredProcessRepresentation(Guid instanceGuid, string representation)
    {
        string instanceUpdate = representation switch
        {
            "status-absent" =>
                "jsonb_set(instance, '{Process}', CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN (instance -> 'Process') - 'Status' ELSE '{}'::jsonb END)",
            "status-null" =>
                "jsonb_set(instance, '{Process}', (CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN instance -> 'Process' ELSE '{}'::jsonb END) || '{\"Status\":null}'::jsonb)",
            "process-absent" => "instance - 'Process'",
            "process-null" => "jsonb_set(instance, '{Process}', 'null'::jsonb)",
            "process-string" => "jsonb_set(instance, '{Process}', '\"legacy\"'::jsonb)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                "Unknown process representation."
            ),
        };

        return PostgresUtil.RunSql(
            $"update storage.instances set instance = {instanceUpdate} where alternateid = '{instanceGuid}'"
        );
    }

    private static Task SetStoredProcessStatus(Guid instanceGuid, ProcessStatus status) =>
        PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Process}}', (CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN instance -> 'Process' ELSE '{{}}'::jsonb END) || jsonb_build_object('Status', '{JsonSerializer.Serialize(status)}'::jsonb)) where alternateid = '{instanceGuid}'"
        );

    private static Task<string> ReadStoredProcessStatus(Guid instanceGuid) =>
        PostgresUtil.RunQuery<string>(
            $"select coalesce(instance -> 'Process' ->> 'Status', 'idle') from storage.instances where alternateid = '{instanceGuid}'"
        );

    private static Task SetStoredProcessStatusRepresentation(
        Guid instanceGuid,
        string statusJson
    ) =>
        PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Process}}', (CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN instance -> 'Process' ELSE '{{}}'::jsonb END) || jsonb_build_object('Status', '{statusJson}'::jsonb)) where alternateid = '{instanceGuid}'"
        );

    private static Task<string> ReadStoredProcessStatusRepresentation(Guid instanceGuid) =>
        PostgresUtil.RunQuery<string>(
            $"select case when jsonb_typeof(instance -> 'Process') = 'object' and (instance -> 'Process') ? 'Status' then (instance -> 'Process' -> 'Status')::text else '<absent>' end from storage.instances where alternateid = '{instanceGuid}'"
        );

    private static Task<string> ReadStoredInstanceJson(Guid instanceGuid) =>
        PostgresUtil.RunQuery<string>(
            $"select instance::text from storage.instances where alternateid = '{instanceGuid}'"
        );

    private static async Task<StorageVersions> ReadStoredVersions(Guid instanceGuid) =>
        new(
            await PostgresUtil.RunQuery<int>(
                $"select instance_version from storage.instances where alternateid = '{instanceGuid}'"
            ),
            await PostgresUtil.RunQuery<int>(
                $"select process_state_version from storage.instances where alternateid = '{instanceGuid}'"
            )
        );

    private async Task WaitForBlockedDatabaseCalls(string queryFragment, int expectedCount)
    {
        await using NpgsqlConnection observerConnection =
            await _instanceFixture.DataSource.OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            """
            select count(*)::int
            from pg_stat_activity activity
            where activity.pid <> pg_backend_pid()
                and activity.datname = current_database()
                and activity.state = 'active'
                and activity.wait_event_type = 'Lock'
                and position($1 in activity.query) > 0
            """,
            observerConnection
        );
        command.Parameters.AddWithValue(NpgsqlDbType.Text, queryFragment);

        DateTime timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (Convert.ToInt32(await command.ExecuteScalarAsync()) < expectedCount)
        {
            if (DateTime.UtcNow >= timeoutAt)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {expectedCount} calls containing '{queryFragment}' to wait on PostgreSQL locks."
                );
            }

            await Task.Delay(10);
        }
    }

    private static InstanceQueryParameters GetDateQueryParams(string fromYear, string toYear)
    {
        return new InstanceQueryParameters
        {
            Size = 100,
            MsgBoxInterval =
            [
                $"gt:{fromYear}-01-01T23:00:00.000Z",
                $"lt:{toYear}-01-12T23:00:00.000Z",
            ],
        };
    }

    private async Task PrepareDateSearch()
    {
        InstanceInternal newInstance1 = TestData.Instance_1_1.Clone().FromApiModel();
        InstanceInternal newInstance2 = TestData.Instance_1_2.Clone().FromApiModel();
        InstanceInternal newInstance3 = TestData.Instance_1_3.Clone().FromApiModel();
        InstanceInternal newInstance4 = TestData.Instance_2_1.Clone().FromApiModel();

        newInstance1.Created = new DateTime(2021, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance2.Created = new DateTime(2022, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance3.LastChanged = new DateTime(2023, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance4.LastChanged = new DateTime(2024, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);

        newInstance1.Status.IsArchived = false;
        newInstance2.Status.IsArchived = false;
        newInstance3.Status.IsArchived = true;
        newInstance4.Status.IsArchived = true;

        await _instanceFixture.InstanceRepo.Create(newInstance1, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance2, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance3, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance4, CancellationToken.None);
    }
}

public class InstanceFixture
{
    public IInstanceRepository InstanceRepo { get; set; }

    public IDataRepository DataRepo { get; set; }

    public NpgsqlDataSource DataSource { get; set; }

    public InstanceFixture()
    {
        var serviceList = ServiceUtil.GetServices(
            new List<Type>()
            {
                typeof(IInstanceRepository),
                typeof(IDataRepository),
                typeof(NpgsqlDataSource),
            }
        );
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
        DataSource = serviceList.OfType<NpgsqlDataSource>().Single();
    }
}
