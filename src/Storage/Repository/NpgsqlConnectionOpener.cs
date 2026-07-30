using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <inheritdoc cref="INpgsqlConnectionOpener"/>
public sealed class NpgsqlConnectionOpener : INpgsqlConnectionOpener
{
    private const int _maxAttempts = 3;
    private static readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(100);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlConnectionOpener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlConnectionOpener"/> class.
    /// </summary>
    public NpgsqlConnectionOpener(
        NpgsqlDataSource dataSource,
        ILogger<NpgsqlConnectionOpener> logger
    )
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await _dataSource.OpenConnectionAsync(cancellationToken);
            }
            catch (NpgsqlException e) when (attempt < _maxAttempts && e.IsTransient)
            {
                TimeSpan delay = _baseDelay * Math.Pow(2, attempt - 1);
                _logger.LogWarning(
                    e,
                    "Transient failure opening PostgreSQL connection (attempt {Attempt} of {MaxAttempts}). Retrying in {Delay}.",
                    attempt,
                    _maxAttempts,
                    delay
                );
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
