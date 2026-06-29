using System.Threading;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Interface for translating service owner codes into organisation numbers using the Altinn CDN.
/// </summary>
public interface IOrganisationService
{
    /// <summary>
    /// Gets the organisation number for the given service owner code.
    /// </summary>
    /// <param name="serviceOwnerCode">The service owner code to look up.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The organisation number, or <c>null</c> if the code is unknown or has no number.</returns>
    Task<string?> GetOrgNumber(string serviceOwnerCode, CancellationToken cancellationToken);
}
