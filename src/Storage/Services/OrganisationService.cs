using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Organisation service
/// </summary>
public class OrganisationService : IOrganisationService
{
    private readonly IOrganisationRepository _organisationRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganisationService"/> class.
    /// </summary>
    /// <param name="organisationRepository">The organisation repository.</param>
    public OrganisationService(IOrganisationRepository organisationRepository)
    {
        _organisationRepository = organisationRepository;
    }

    /// <inheritdoc/>
    public async Task<string?> GetOrgNumber(
        string serviceOwnerCode,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(serviceOwnerCode);

        IReadOnlyDictionary<string, Org> organisations =
            await _organisationRepository.GetOrganisations(cancellationToken);

        return organisations.TryGetValue(serviceOwnerCode, out Org? org) ? org.Orgnr : null;
    }
}
