using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Altinn.AccessManagement.Core.Models;
using Altinn.Platform.Storage.Configuration;
using AltinnCore.Authentication.Constants;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Middleware for enriching telemetry with user claims and route values.
/// </summary>
internal sealed class TelemetryEnrichingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TelemetryEnrichingMiddleware> _logger;
    private static readonly FrozenDictionary<string, Action<Claim, Activity>> _claimActions = InitClaimActions();
    private readonly bool _disableTelemetryForMigration;

    private static FrozenDictionary<string, Action<Claim, Activity>> InitClaimActions()
    {
        var actions = new Dictionary<string, Action<Claim, Activity>>(StringComparer.OrdinalIgnoreCase)
        {
            {
                AltinnCoreClaimTypes.UserId,
                static (claim, activity) =>
                {
                    activity.SetTag("user.id", claim.Value);
                }
            },
            {
                AltinnCoreClaimTypes.PartyID,
                static (claim, activity) =>
                {
                    activity.SetTag("user.party.id", claim.Value);
                }
            },
            {
                AltinnCoreClaimTypes.AuthenticationLevel,
                static (claim, activity) =>
                {
                    activity.SetTag("user.authentication.level", claim.Value);
                }
            },
            {
                AltinnCoreClaimTypes.Org,
                static (claim, activity) =>
                {
                    activity.SetTag("user.application.owner.id", claim.Value);
                }
            },
            {
                AltinnCoreClaimTypes.OrgNumber,
                static (claim, activity) =>
                {
                    activity.SetTag("user.organization.number", claim.Value);
                }
            },
            {
                "authorization_details",
                static (claim, activity) =>
                {
                    SystemUserClaim claimValue = JsonSerializer.Deserialize<SystemUserClaim>(claim.Value);
                    activity.SetTag("user.system.id", claimValue?.Systemuser_id[0] ?? null);
                    activity.SetTag("user.system.owner.number", claimValue?.Systemuser_org.ID ?? null);
                }
            },
        };

        return actions.ToFrozenDictionary();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryEnrichingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="generalSettings">Configuration object used to hold general settings for the storage application.</param>
    public TelemetryEnrichingMiddleware(RequestDelegate next, ILogger<TelemetryEnrichingMiddleware> logger, IOptions<GeneralSettings> generalSettings)
    {
        _next = next;
        _logger = logger;
        _disableTelemetryForMigration = generalSettings.Value.DisableTelemetryForMigration;
    }

    /// <summary>
    /// Invokes the middleware to process the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var activity = context.Features.Get<IHttpActivityFeature>()?.Activity;
        if (activity is null || !context.Request.Path.ToString().Contains("storage/api/"))
        {
            await _next(context);
            return;
        }

        if (_disableTelemetryForMigration && context.Request is not null && context.Request.Path.ToUriComponent().StartsWith("/storage/api/v1/migration", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
        }

        try
        {
            foreach (var claim in context.User.Claims)
            {
                if (_claimActions.TryGetValue(claim.Type, out var action))
                {
                    action(claim, activity);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while enriching telemetry.");
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for adding the <see cref="TelemetryEnrichingMiddleware"/> to the application pipeline.
/// </summary>
public static class TelemetryEnrichingMiddlewareExtensions
{
    /// <summary>
    /// Adds the <see cref="TelemetryEnrichingMiddleware"/> to the application's request pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseTelemetryEnricher(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TelemetryEnrichingMiddleware>(
            app.ApplicationServices.GetRequiredService<ILogger<TelemetryEnrichingMiddleware>>(), app.ApplicationServices.GetRequiredService<IOptions<GeneralSettings>>());
    }
}
