using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Register.Contracts;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Populates instance owners from Altinn Register.
/// </summary>
public class InstanceOwnerService : IInstanceOwnerService
{
    private readonly IRegisterService _registerService;
    private readonly IInstanceRepository _instanceRepository;
    private readonly ILogger<InstanceOwnerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceOwnerService"/> class.
    /// </summary>
    public InstanceOwnerService(
        IRegisterService registerService,
        IInstanceRepository instanceRepository,
        ILogger<InstanceOwnerService> logger
    )
    {
        _registerService = registerService;
        _instanceRepository = instanceRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InstanceOwner?> GetInstanceOwner(
        PartyUrn identifier,
        CancellationToken cancellationToken
    )
    {
        Party? party = await _registerService.GetParty(identifier, cancellationToken);
        return party is null ? null : InstanceOwnerMapper.FromParty(party);
    }

    /// <inheritdoc/>
    public async Task PopulateMissingInstanceOwner(
        InstanceInternal instance,
        CancellationToken cancellationToken
    )
    {
        if (
            instance.InstanceOwner?.PartyUuid is not null
            || !int.TryParse(
                instance.InstanceOwner?.PartyId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int partyId
            )
        )
        {
            return;
        }

        try
        {
            InstanceOwner? instanceOwner = await GetInstanceOwner(
                PartyUrn.PartyId.Create((uint)partyId),
                cancellationToken
            );
            if (instanceOwner is null)
            {
                _logger.LogWarning(
                    "Unable to populate the owner of instance {InstanceId}: party {PartyId} was not found in Register",
                    instance.Id,
                    partyId
                );
                return;
            }

            instance.InstanceOwner = instanceOwner;
            await _instanceRepository.UpsertInstanceOwner(instanceOwner, cancellationToken);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogError(
                ex,
                "Unable to populate the owner of instance {InstanceId} for party {PartyId}",
                instance.Id,
                partyId
            );
        }
    }
}
