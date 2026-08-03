using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Interface for accessing the Altinn CDN organisation list.
/// </summary>
public interface IOrganisationRepository
{
    /// <summary>
    /// Gets the organisations registered on the Altinn CDN, keyed by service owner code.
    /// The result is cached.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>A dictionary of organisations keyed by service owner code (e.g. "ttd").</returns>
    Task<IReadOnlyDictionary<string, Org>> GetOrganisations(CancellationToken cancellationToken);
}
