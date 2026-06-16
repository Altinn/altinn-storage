using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Extensions;

internal static class HostingExtensions
{
    /// <summary>
    /// Configures host shutdown to coordinate Kubernetes endpoint drain and ASP.NET Core request drain.
    /// </summary>
    internal static WebApplicationBuilder UseGracefulShutdown(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var shutdownDelay = TimeSpan.FromSeconds(5);
        var shutdownTimeout = TimeSpan.FromSeconds(20);

        builder.Services.AddSingleton<IHostLifetime>(sp =>
            ActivatorUtilities.CreateInstance<AppHostLifetime>(sp, shutdownDelay)
        );

        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = shutdownTimeout
        );

        return builder;
    }

    private sealed class AppHostLifetime(
        ILogger<AppHostLifetime> logger,
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        TimeSpan delay
    ) : IHostLifetime, IDisposable
    {
        private IDisposable[]? _disposables;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(
                !environment.IsDevelopment(),
                "We don't need graceful shutdown in development environments"
            );
            PosixSignalRegistration? sigint = null;
            PosixSignalRegistration? sigquit = null;
            PosixSignalRegistration? sigterm = null;
            try
            {
#pragma warning disable CA2000 // Ownership is transferred to _disposables and disposed in Dispose().
                sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleSignal);
                sigquit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, HandleSignal);
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleSignal);
#pragma warning restore CA2000
                _disposables = [sigint, sigquit, sigterm];
            }
            catch
            {
                TryDispose(sigint);
                TryDispose(sigquit);
                TryDispose(sigterm);
                throw;
            }

            return Task.CompletedTask;
        }

        private void HandleSignal(PosixSignalContext ctx)
        {
            logger.LogInformation(
                "Received shutdown signal: {Signal}, delaying shutdown",
                ctx.Signal
            );
            ctx.Cancel = true;

            _ = Task.Delay(delay)
                .ContinueWith(
                    _ =>
                    {
                        logger.LogInformation("Starting host shutdown...");
                        applicationLifetime.StopApplication();
                    },
                    TaskScheduler.Default
                );
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables ?? [])
            {
                TryDispose(disposable);
            }
        }

        private void TryDispose(IDisposable? disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error during disposal of {Type}",
                    disposable?.GetType().FullName
                );
            }
        }
    }
}
