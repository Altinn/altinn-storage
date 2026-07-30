using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Opens pooled PostgreSQL connections for the repositories. Use this rather than calling
/// <see cref="NpgsqlDataSource"/> directly, so the retry applies uniformly across the data layer.
/// </summary>
public interface INpgsqlConnectionOpener
{
    /// <summary>
    /// Opens a connection from the pool, retrying transient failures with backoff.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Only the open is retried, and anything the open itself runs is idempotent session setup,
    /// so this is safe for writes as well as reads.
    /// </remarks>
    /// <returns>An open connection. The caller owns it and must dispose it.</returns>
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}
