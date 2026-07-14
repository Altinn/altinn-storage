#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Service class with business logic related to signing
/// </summary>
public class SigningService : ISigningService
{
    private readonly IInstanceRepository _instanceRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly ILogger<SigningService> _logger;
    private readonly IDataService _dataService;
    private readonly IApplicationService _applicationService;
    private readonly IInstanceEventService _instanceEventService;
    private readonly IInstanceMutationRepository _instanceMutationRepository;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new(
        JsonSerializerOptions.Web
    )
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SigningService"/> class.
    /// </summary>
    public SigningService(
        IInstanceRepository instanceRepository,
        IDataService dataService,
        IApplicationService applicationService,
        IInstanceEventService instanceEventService,
        IInstanceMutationRepository instanceMutationRepository,
        IApplicationRepository applicationRepository,
        IBlobRepository blobRepository,
        ILogger<SigningService> logger
    )
    {
        _instanceRepository = instanceRepository;
        _dataService = dataService;
        _applicationService = applicationService;
        _instanceEventService = instanceEventService;
        _instanceMutationRepository = instanceMutationRepository;
        _applicationRepository = applicationRepository;
        _blobRepository = blobRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SignDocumentCreateResult> CreateSignDocument(
        Guid instanceGuid,
        SignRequest signRequest,
        string performedBy,
        CancellationToken cancellationToken,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            true,
            cancellationToken
        );

        if (instance == null)
        {
            return SignDocumentCreateResult.Failure(new ServiceError(404, "Instance not found"));
        }

        StorageVersions currentVersions = instance.Versions;
        if (
            expectedInstanceVersion is not null
            && expectedInstanceVersion != currentVersions.InstanceVersion
        )
        {
            return SignDocumentCreateResult.PreconditionFailed(
                new ServiceError(412, "instance_version_mismatch"),
                currentVersions
            );
        }

        if (
            expectedProcessStateVersion is not null
            && expectedProcessStateVersion != currentVersions.ProcessStateVersion
        )
        {
            return SignDocumentCreateResult.PreconditionFailed(
                new ServiceError(412, "process_state_version_mismatch"),
                currentVersions
            );
        }

        Application app = await _applicationRepository.FindOne(
            instance.AppId,
            instance.Org,
            cancellationToken
        );

        (bool validDataType, ServiceError serviceError) =
            await _applicationService.ValidateDataTypeForApp(
                instance.Org,
                instance.AppId,
                signRequest.SignatureDocumentDataType,
                instance.Process.CurrentTask?.ElementId
            );
        if (!validDataType)
        {
            return SignDocumentCreateResult.Failure(serviceError, currentVersions);
        }

        SignDocument signDocument = CreateSignDocument(instanceGuid, signRequest);

        foreach (
            SignRequest.DataElementSignature dataElementSignature in signRequest.DataElementSignatures
        )
        {
            (string base64Sha256Hash, serviceError) = await _dataService.GenerateSha256Hash(
                instance.Org,
                instanceGuid,
                Guid.Parse(dataElementSignature.DataElementId),
                app.StorageAccountNumber
            );
            if (string.IsNullOrEmpty(base64Sha256Hash))
            {
                return SignDocumentCreateResult.Failure(serviceError, currentVersions);
            }

            signDocument.DataElementSignatures.Add(
                new SignDocument.DataElementSignature
                {
                    DataElementId = dataElementSignature.DataElementId,
                    Sha256Hash = base64Sha256Hash,
                    Signed = dataElementSignature.Signed,
                }
            );
        }

        Guid signDocumentDataElementId = Guid.NewGuid();
        signDocument.Id = signDocumentDataElementId.ToString();

        SignDocDownloadResult existingSignDocument = await FindExistingSignDocumentForSignee(
            instance,
            app,
            signRequest.SignatureDocumentDataType,
            signDocument.SigneeInfo,
            cancellationToken
        );

        StagedDataElementBlob stagedDataElement;
        using (var fileStream = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(
                fileStream,
                signDocument,
                _jsonSerializerOptions,
                cancellationToken
            );

            fileStream.Position = 0;
            stagedDataElement = await _dataService.StageDataElementBlob(
                instance,
                fileStream,
                new DataElementCreateOptions
                {
                    DataElementId = signDocumentDataElementId,
                    DataType = signRequest.SignatureDocumentDataType,
                    ContentType = "application/json",
                    Filename = $"{signRequest.SignatureDocumentDataType}.json",
                    Created = signDocument.SignedTime,
                    CreatedBy = performedBy,
                    GeneratedFromTask = signRequest.GeneratedFromTask,
                    Locked = true,
                },
                app.StorageAccountNumber,
                cancellationToken
            );
        }

        bool applyAttempted = false;
        InstanceMutationApplyResult applyResult;
        try
        {
            List<InstanceEvent> instanceEvents =
            [
                _instanceEventService.BuildInstanceEvent(InstanceEventType.Signed, instance),
            ];
            if (existingSignDocument is not null)
            {
                instanceEvents.Add(
                    _instanceEventService.BuildInstanceEvent(
                        InstanceEventType.Deleted,
                        instance,
                        existingSignDocument.DataElement
                    )
                );
            }

            InstanceMutationCommit mutation = new(
                [stagedDataElement.DataElement],
                [],
                existingSignDocument is null
                    ? []
                    :
                    [
                        new InstanceMutationDataElementDelete(
                            existingSignDocument.DataElement,
                            IgnoreLock: true
                        ),
                    ],
                instance,
                [],
                expectedInstanceVersion,
                expectedProcessStateVersion,
                instanceEvents,
                null
            );

            applyAttempted = true;
            applyResult = await _instanceMutationRepository.Apply(
                instanceGuid,
                instance.InternalId,
                mutation,
                cancellationToken
            );
        }
        catch (StorageVersionMismatchException exception)
        {
            await _dataService.DeleteStagedDataElementBlob(
                instance,
                stagedDataElement.DataElement,
                app.StorageAccountNumber
            );
            StorageVersions versions = new(
                exception.CurrentInstanceVersion,
                exception.CurrentProcessStateVersion
            );

            return SignDocumentCreateResult.PreconditionFailed(
                new ServiceError(412, GetVersionMismatchErrorMessage(exception)),
                versions
            );
        }
        catch (Exception exception)
        {
            if (!applyAttempted || DataService.IndicatesDefiniteRollback(exception))
            {
                await _dataService.DeleteStagedDataElementBlob(
                    instance,
                    stagedDataElement.DataElement,
                    app.StorageAccountNumber
                );
            }

            throw;
        }

        InstanceInternal updatedInstance = applyResult.Instance;

        if (existingSignDocument is not null)
        {
            await _dataService.CleanupDeletedDataElementBlobs(
                updatedInstance,
                existingSignDocument.DataElement,
                app.StorageAccountNumber,
                CancellationToken.None
            );
        }

        return SignDocumentCreateResult.Success(updatedInstance.Versions);
    }

    private async Task<SignDocDownloadResult> FindExistingSignDocumentForSignee(
        InstanceInternal instance,
        Application application,
        string signDocDataType,
        Signee signee,
        CancellationToken cancellationToken
    )
    {
        List<DataElementInternal> signingDocDataElements =
            instance.Data?.Where(x => x.DataType == signDocDataType).ToList() ?? [];

        List<Task<SignDocDownloadResult>> downloadAndDeserializeSignDocumentTasks =
            signingDocDataElements
                .Select(async dataElement =>
                {
                    try
                    {
                        await using Stream stream = await _blobRepository.ReadBlob(
                            instance.Org,
                            dataElement.BlobStoragePath,
                            application.StorageAccountNumber,
                            cancellationToken
                        );
                        var signDocument = await JsonSerializer.DeserializeAsync<SignDocument>(
                            stream,
                            cancellationToken: cancellationToken
                        );
                        return new SignDocDownloadResult
                        {
                            DataElement = dataElement,
                            SignDocument = signDocument,
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error reading or deserializing blob for DataElement {DataElementId} while checking for existing signature.",
                            dataElement.Id
                        );
                        return null;
                    }
                })
                .ToList();

        SignDocDownloadResult[] results = await Task.WhenAll(
            downloadAndDeserializeSignDocumentTasks
        );

        foreach (SignDocDownloadResult result in results)
        {
            if (
                result?.SignDocument is null
                || !SigneesAreEqual(result.SignDocument.SigneeInfo, signee)
            )
            {
                continue;
            }

            _logger.LogInformation(
                "Sign document already exists for this signee and will be replaced. Data element id: {DataElementId}",
                result.DataElement.Id
            );

            return result;
        }

        return null;
    }

    private static SignDocument CreateSignDocument(Guid instanceGuid, SignRequest signRequest)
    {
        var signDocument = new SignDocument
        {
            InstanceGuid = instanceGuid.ToString(),
            SignedTime = DateTime.UtcNow,
            SigneeInfo = new Signee
            {
                UserId = signRequest.Signee.UserId,
                PersonNumber = signRequest.Signee.PersonNumber,
                OrganisationNumber = signRequest.Signee.OrganisationNumber,
                SystemUserId = signRequest.Signee.SystemUserId,
            },
        };

        return signDocument;
    }

    private static bool SigneesAreEqual(Signee signee1, Signee signee2) =>
        signee1 is not null
        && signee2 is not null
        && signee1.UserId == signee2.UserId
        && signee1.SystemUserId == signee2.SystemUserId
        && signee1.PersonNumber == signee2.PersonNumber
        && signee1.OrganisationNumber == signee2.OrganisationNumber;

    private static string GetVersionMismatchErrorMessage(
        StorageVersionMismatchException exception
    ) =>
        exception is InstanceVersionMismatchException
            ? "instance_version_mismatch"
            : "process_state_version_mismatch";

    private sealed record SignDocDownloadResult
    {
        public DataElementInternal DataElement { get; init; }

        public SignDocument SignDocument { get; init; }
    }
}
