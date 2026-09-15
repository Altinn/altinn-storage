#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <inheritdoc/>
public class DataElementContentService : IDataElementContentService
{
    private readonly IInstanceRepository _instanceRepository;
    private readonly IDataRepository _dataRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly IAuthorization _authorizationService;
    private readonly IOnDemandContentService _onDemandContentService;
    private readonly GeneralSettings _generalSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataElementContentService"/> class
    /// </summary>
    /// <param name="instanceRepository">the instance repository</param>
    /// <param name="dataRepository">the data repository handler</param>
    /// <param name="applicationRepository">the application repository</param>
    /// <param name="blobRepository">the blob repository handler</param>
    /// <param name="authorizationService">The authorization service</param>
    /// <param name="onDemandContentService">generates on demand content for migrated Altinn 2 data elements</param>
    /// <param name="generalSettings">the general settings.</param>
    public DataElementContentService(
        IInstanceRepository instanceRepository,
        IDataRepository dataRepository,
        IApplicationRepository applicationRepository,
        IBlobRepository blobRepository,
        IAuthorization authorizationService,
        IOnDemandContentService onDemandContentService,
        IOptions<GeneralSettings> generalSettings
    )
    {
        _instanceRepository = instanceRepository;
        _dataRepository = dataRepository;
        _applicationRepository = applicationRepository;
        _blobRepository = blobRepository;
        _authorizationService = authorizationService;
        _onDemandContentService = onDemandContentService;
        _generalSettings = generalSettings.Value;
    }

    /// <inheritdoc/>
    public async Task<(DataElementReadContext Context, ServiceError ServiceError)> ResolveForRead(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            false,
            cancellationToken
        );
        if (instance is null)
        {
            return (
                null,
                new ServiceError(
                    404,
                    $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}."
                )
            );
        }

        if (!await _authorizationService.AuthorizeEnrichedInstanceAction(instance, "read"))
        {
            return (null, new ServiceError(403, "Not authorized to read the instance"));
        }

        DataElementInternal dataElement = await _dataRepository.Read(
            instanceGuid,
            dataGuid,
            cancellationToken
        );
        if (dataElement is null)
        {
            return (
                null,
                new ServiceError(404, $"Unable to find any data element with id: {dataGuid}.")
            );
        }

        Application application = await _applicationRepository.FindOne(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application is null)
        {
            return (
                null,
                new ServiceError(404, $"Cannot find application {instance.AppId} in storage")
            );
        }

        DataType dataTypeDefinition = application.DataTypes.FirstOrDefault(dataType =>
            dataType.Id == dataElement.DataType
        );
        if (dataTypeDefinition is null)
        {
            return (
                null,
                new ServiceError(
                    400,
                    "Requested element type is not declared in application metadata"
                )
            );
        }

        if (!await dataTypeDefinition.CanRead(_authorizationService, instance))
        {
            return (
                null,
                new ServiceError(403, $"Not authorized to read data of type {dataElement.DataType}")
            );
        }

        return (
            new DataElementReadContext(instanceGuid, dataGuid, instance, dataElement, application),
            null
        );
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenContent(
        DataElementReadContext context,
        string language,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = context.Instance;
        DataElementInternal dataElement = context.DataElement;

        if (context.IsOnDemandContent)
        {
            return await _onDemandContentService.GetContent(
                dataElement.BlobStoragePath.Split('/')[1],
                instance.AppId.Split('/')[1],
                context.InstanceGuid,
                context.DataGuid,
                language,
                cancellationToken
            );
        }

        DataElementHelper.EnsureBlobStoragePathMatchesRequest(
            dataElement,
            instance.AppId,
            context.InstanceGuid,
            context.DataGuid
        );

        return await _blobRepository.ReadBlob(
            BlobStorageOrg(instance),
            dataElement.BlobStoragePath,
            context.Application.StorageAccountNumber,
            cancellationToken
        );
    }

    private string BlobStorageOrg(InstanceInternal instance)
    {
        bool migratedFromAltinn2 =
            instance.AppId.Contains(@"/a1-") || instance.AppId.Contains(@"/a2-");

        return migratedFromAltinn2 && _generalSettings.A2UseTtdAsServiceOwner
            ? "ttd"
            : instance.Org;
    }
}
