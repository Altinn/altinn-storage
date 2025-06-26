using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Authorizes access to application instances based on current request context token scopes.
/// </summary>
internal sealed class ApiScopeAuthorizer : BackgroundService, IApiScopeAuthorizer
{
    /// <summary>
    /// Scope added by altinn-authentication identifying logins through the Altinn portal.
    /// </summary>
    public static readonly string PortalUserApiScope = "altinn:portal/enduser";

    private readonly ILogger<ApiScopeAuthorizer> _logger;
    private readonly IOptionsMonitor<GeneralSettings> _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApplicationRepository _applicationRepository;

    private sealed record AppCache(Dictionary<string, Application> Apps, Dictionary<string, HashSet<string>> CustomScopes);

    private AppCache _appsCache = new(Apps: new(StringComparer.Ordinal), CustomScopes: new(StringComparer.Ordinal));

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiScopeAuthorizer"/> class.
    /// </summary>
    /// <param name="logger">logger</param>
    /// <param name="settings">general settings</param>
    /// <param name="httpContextAccessor">http context accessor</param>
    /// <param name="applicationRepository">app repo</param>
    public ApiScopeAuthorizer(ILogger<ApiScopeAuthorizer> logger, IOptionsMonitor<GeneralSettings> settings, IHttpContextAccessor httpContextAccessor, IApplicationRepository applicationRepository)
    {
        _logger = logger;
        _settings = settings;
        _httpContextAccessor = httpContextAccessor;
        _applicationRepository = applicationRepository;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try 
        {
            var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            do 
            {
                try
                {
                    Dictionary<string, Application> apps = new(StringComparer.Ordinal);
                    Dictionary<string, HashSet<string>> customScopes = new(StringComparer.Ordinal);

                    var allApps = await _applicationRepository.FindAll();

                    foreach (var app in allApps)
                    {
                        apps.Add(app.Id, app);

                        if (!string.IsNullOrEmpty(app.ApiScopes?.Read))
                        {
                            if (!customScopes.TryGetValue(app.ApiScopes.Read, out var appsForScope))
                            {
                                var added = customScopes.TryAdd(app.ApiScopes.Read, appsForScope = new(1));
                                Debug.Assert(added, "There is no concurrent mutations, so this shouldn't fail");
                            }

                            appsForScope.Add(app.Id);
                        }

                        if (!string.IsNullOrEmpty(app.ApiScopes?.Write))
                        {
                            if (!customScopes.TryGetValue(app.ApiScopes.Write, out var appsForScope))
                            {
                                var added = customScopes.TryAdd(app.ApiScopes.Write, appsForScope = new(1));
                                Debug.Assert(added, "There is no concurrent mutations, so this shouldn't fail");
                            }

                            appsForScope.Add(app.Id);
                        }
                    }

                    // For each iteration of this loop we rebuild the entire cache from current DB state.
                    var cache = new AppCache(apps, customScopes);
                    Volatile.Write(ref _appsCache, cache);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error in ApiScopeAuthorizer");
                }
            } 
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error starting ApiScopeAuthorizer service");
        }
    }

    /// <inheritdoc/>
    public Task<bool> Authorize(InstanceOperation operation, Instance instance) => Authorize(operation, instance.AppId);

    /// <inheritdoc/>
    public async Task<bool> Authorize(InstanceOperation operation, string appId)
    {
        var authorizeDefaultApiScopes = _settings.CurrentValue.AuthorizeDefaultApiScopes;

        var user = _httpContextAccessor.HttpContext.User;
        if (user is null || !user.Identity.IsAuthenticated)
        {
            if (!authorizeDefaultApiScopes)
            {
                return true;
            }

            return false;
        }

        var scopeClaim = user.FindFirst("urn:altinn:scope")?.Value;
        scopeClaim ??= user.FindFirst("scope")?.Value;
        var scopes = new Scopes(scopeClaim);

        if (scopes.HasScope(PortalUserApiScope))
        {
            // This scope is owned by Altinn portal (digdir)
            // and should give access to all apps in Altinn (you don't choose an app by logging in)
            return true;
        }

        if (user.GetOrg() is not null)
        {
            if (!authorizeDefaultApiScopes)
            {
                return true;
            }

            // For service owner tokens, we need a 'serviceowner' variant
            // of the default scopes (there is no other config for this)
            return scopes.HasScope($"altinn:serviceowner/instances.{operation.ToScopeOperation()}");
        }

        if (appId.Split('/') is not [var org, _])
        {
            throw new Exception($"Invalid appId format, expected 'org/appName': {appId}");
        }

        var appsCache = Volatile.Read(ref _appsCache);
        var apps = appsCache.Apps;
        var customScopes = appsCache.CustomScopes;

        var app = apps.GetValueOrDefault(appId) ?? await _applicationRepository.FindOne(appId, org);
        if (app is null)
        {
            throw new InvalidOperationException($"Application {appId} not found");
        }

        var customScope = operation == InstanceOperation.Read ? app.ApiScopes?.Read : app.ApiScopes?.Write;
        if (!string.IsNullOrWhiteSpace(customScope))
        {
            // If the app for the instance requires a custom scope, the current token must have it
            return scopes.HasScope(customScope);
        }

        // Now we know the current instance's app doesn't require a custom scope,
        // but the current token may have a scope that is associated with another app,
        // then we should deny access to this instance's app if it is meant for another app
        var appsByScope = customScopes.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var scope in scopes)
        {
            if (appsByScope.TryGetValue(scope, out var appsForScope))
            {
                if (appsForScope.Contains(app.Id))
                {
                    // We raced against the timer based cache update,
                    // and the current app now requires the scope that the current token has
                    return true;
                }
                
                return false;
            }
        }

        if (!authorizeDefaultApiScopes)
        {
            return true;
        }

        // Finally if the current app doesn't have a custom scope,
        // and the current token doesn't have a scope belonging to a different app,
        // then we should make sure
        return scopes.HasScope($"altinn:instances.{operation.ToScopeOperation()}");
    }
}
