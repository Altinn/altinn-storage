#nullable disable

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <inheritdoc/>
public class DataElementContentService : IDataElementContentService
{
    private readonly IBlobRepository _blobRepository;
    private readonly IOnDemandContentService _onDemandContentService;
    private readonly GeneralSettings _generalSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataElementContentService"/> class
    /// </summary>
    /// <param name="blobRepository">the blob repository handler</param>
    /// <param name="onDemandContentService">generates on demand content for migrated Altinn 2 data elements</param>
    /// <param name="generalSettings">the general settings.</param>
    public DataElementContentService(
        IBlobRepository blobRepository,
        IOnDemandContentService onDemandContentService,
        IOptions<GeneralSettings> generalSettings
    )
    {
        _blobRepository = blobRepository;
        _onDemandContentService = onDemandContentService;
        _generalSettings = generalSettings.Value;
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenContent(
        InstanceInternal instance,
        DataElementInternal dataElement,
        Application application,
        string language,
        CancellationToken cancellationToken
    )
    {
        if (DataElementHelper.IsOnDemandContent(dataElement))
        {
            return await _onDemandContentService.GetContent(
                dataElement.BlobStoragePath.Split('/')[1],
                instance.AppId.Split('/')[1],
                instance.Id,
                dataElement.Id,
                language,
                cancellationToken
            );
        }

        DataElementHelper.EnsureBlobStoragePathMatchesRequest(
            dataElement,
            instance.AppId,
            instance.Id,
            dataElement.Id
        );

        return await _blobRepository.ReadBlob(
            BlobStorageOrg(instance),
            dataElement.BlobStoragePath,
            application.StorageAccountNumber,
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
