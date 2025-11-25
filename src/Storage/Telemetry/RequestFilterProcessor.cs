using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Altinn.AccessManagement.Core.Models;
using Altinn.Platform.Storage.Configuration;
using AltinnCore.Authentication.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Filter for requests (and child dependencies) that should not be logged.
/// </summary>
public class RequestFilterProcessor : BaseProcessor<Activity>
{
    private const string RequestKind = "Microsoft.AspNetCore.Hosting.HttpRequestIn";
    private readonly bool _disableTelemetryForMigration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly FrozenDictionary<string, Action<Claim, Activity>> _claimActions =
        InitClaimActions();

    private static FrozenDictionary<string, Action<Claim, Activity>> InitClaimActions()
    {
        var actions = new Dictionary<string, Action<Claim, Activity>>(
            StringComparer.OrdinalIgnoreCase
        )
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
                    SystemUserClaim claimValue = JsonSerializer.Deserialize<SystemUserClaim>(
                        claim.Value
                    );
                    activity.SetTag("user.system.id", claimValue?.Systemuser_id[0] ?? null);
                    activity.SetTag(
                        "user.system.owner.number",
                        claimValue?.Systemuser_org.ID ?? null
                    );
                }
            },
        };

        return actions.ToFrozenDictionary();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestFilterProcessor"/> class.
    /// </summary>
    public RequestFilterProcessor(
        GeneralSettings generalSettings,
        IHttpContextAccessor httpContextAccessor = null
    )
        : base()
    {
        _disableTelemetryForMigration = generalSettings.DisableTelemetryForMigration;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Determine whether to skip a request
    /// </summary>
    public override void OnStart(Activity activity)
    {
        bool skip = false;
        if (activity.OperationName == RequestKind)
        {
            skip = ExcludeRequest(_httpContextAccessor.HttpContext.Request.Path.Value);
        }
        else if (
            !(activity.Parent?.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ?? true)
        )
        {
            skip = true;
        }

        if (skip)
        {
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    /// <summary>
    /// No action on end
    /// </summary>
    /// <param name="activity">xx</param>
    public override void OnEnd(Activity activity)
    {
        if (activity.OperationName == RequestKind && _httpContextAccessor.HttpContext is not null)
        {
            if (
                _httpContextAccessor.HttpContext.Request.Headers.TryGetValue(
                    "X-Forwarded-For",
                    out StringValues ipAddress
                )
            )
            {
                activity.SetTag("ipAddress", ipAddress.FirstOrDefault());
            }

            foreach (var claim in _httpContextAccessor.HttpContext.User.Claims)
            {
                if (_claimActions.TryGetValue(claim.Type, out var action))
                {
                    action(claim, activity);
                }
            }
        }
    }

    private bool ExcludeRequest(string localpath)
    {
        return localpath switch
        {
            var path
                when _disableTelemetryForMigration
                    && path.TrimEnd('/').EndsWith("/health", StringComparison.OrdinalIgnoreCase) =>
                true,
            "/storage/api/v1/migration" => true,
            _ => false,
        };
    }
}
