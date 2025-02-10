using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Operations that can be performed on an instance in terms of scope.
/// </summary>
public enum InstanceOperation
{
    /// <summary>
    /// Read operation
    /// </summary>
    Read,
    
    /// <summary>
    /// Write operation
    /// </summary>
    Write,
}

/// <summary>
/// Authorizes access to application instances based on current request context token scopes.
/// </summary>
internal sealed class ApiScopeAuthorizer : BackgroundService, IApiScopeAuthorizer
{
    private readonly ILogger<ApiScopeAuthorizer> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApplicationRepository _applicationRepository;

    private readonly ConcurrentDictionary<string, List<Application>> _customScopes = new ConcurrentDictionary<string, List<Application>>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiScopeAuthorizer"/> class.
    /// </summary>
    /// <param name="logger">logger</param>
    /// <param name="httpContextAccessor">http context accessor</param>
    /// <param name="applicationRepository">app repo</param>
    public ApiScopeAuthorizer(ILogger<ApiScopeAuthorizer> logger, IHttpContextAccessor httpContextAccessor, IApplicationRepository applicationRepository)
    {
        _logger = logger;
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
                    var apps = await _applicationRepository.FindAll();

                    foreach (var app in apps)
                    {
                        if (!string.IsNullOrEmpty(app.ApiScopes?.Read))
                        {
                            if (!_customScopes.TryGetValue(app.ApiScopes.Read, out var appsForScope))
                            {
                                var added = _customScopes.TryAdd(app.ApiScopes.Read, appsForScope = new(1));
                                Debug.Assert(added, "There is no concurrent mutations, so this shouldn't fail");
                            }

                            appsForScope.Add(app);
                        }

                        if (!string.IsNullOrEmpty(app.ApiScopes?.Write))
                        {
                            if (!_customScopes.TryGetValue(app.ApiScopes.Write, out var appsForScope))
                            {
                                var added = _customScopes.TryAdd(app.ApiScopes.Write, appsForScope = new(1));
                                Debug.Assert(added, "There is no concurrent mutations, so this shouldn't fail");
                            }

                            appsForScope.Add(app);
                        }
                    }
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
        var user = _httpContextAccessor.HttpContext.User;
        if (user is null || !user.Identity.IsAuthenticated)
        {
            return false;
        }

        var scopeClaim = _httpContextAccessor.HttpContext.User.FindFirst("scope")?.Value;
        var scopes = new Scopes(scopeClaim);

        if (scopes.HasScope("altinn:portal/enduser"))
        {
            // This scope is owned by Altinn portal (digdir)
            // and should give access to all apps in Altinn (you don't choose an app by logging in)
            return true;
        }

        if (user.GetOrg() is not null)
        {
            // For service owner tokens, we need a 'serviceowner' variant
            // of the default scopes (there is no other config for this)
            // TODO: breaking change, is this safe?
            // TODO: should we have ApiScopes.ServiceOwnerRead?
            return scopes.HasScope($"altinn:serviceowner/instances.{operation.ToString().ToLowerInvariant()}");
        }

        if (appId.Split('/') is not [var org, var appName])
        {
            throw new Exception($"Invalid appId format, expected 'org/appName': {appId}");
        }

        var app = await _applicationRepository.FindOne(appName, org);
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
        var appsByScope = _customScopes.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var scope in scopes)
        {
            if (appsByScope.TryGetValue(scope, out var apps))
            {
                if (apps.Any(a => a.Id == app.Id))
                {
                    // We raced against the timer based cache update,
                    // and the current app now requires the scope that the current token has
                    _logger.LogWarning(
                        "Went looking for custom scope '{Scope}' for app different from '{AppId}', but found it in the cache (there was a race)", 
                        scope.ToString(), 
                        app.Id);
                    return true;
                }
                
                return false;
            }
        }

        // Finally if the current app doesn't have a custom scope,
        // and the current token doesn't have a scope belonging to a different app,
        // then we should make sure
        return scopes.HasScope($"altinn:instances.{operation.ToString().ToLowerInvariant()}");
    }
}
