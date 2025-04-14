#nullable enable
using System;
using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Emits the 'http.server.request.scopes.errors' counter metric
/// to indicate how many requests are made to an authorized endpoint without proper scopes.
/// </summary>
file sealed class LogRequestsWithInvalidScopesFilter(IOptions<GeneralSettings> generalSetings) : IAsyncActionFilter
{
    private readonly IOptions<GeneralSettings> _generalSettings = generalSetings;

    /// <summary>
    /// A key to indicate if the endpoint is authorized
    /// </summary>
    internal static readonly object AuthorizedEndpointKey = new();

    private static readonly Counter<long> _counter = Metrics.Meter.CreateCounter<long>(
        "http.server.request.scopes.errors", 
        "count", 
        "Count of HTTP requests without a valid scope claim");

    private static readonly FrozenSet<string> _acceptedReadScopes = 
        FrozenSet.Create<string>(
            StringComparer.OrdinalIgnoreCase, 
            "altinn:portal/enduser", 
            "altinn:instances.read", 
            "altinn:serviceowner/instances.read");

    private static readonly FrozenSet<string> _acceptedWriteScopes = 
        FrozenSet.Create<string>(
            StringComparer.OrdinalIgnoreCase, 
            "altinn:portal/enduser", 
            "altinn:instances.write", 
            "altinn:serviceowner/instances.write");

    private static readonly FrozenSet<string> _readHttpMethods = 
        FrozenSet.Create<string>(
            StringComparer.OrdinalIgnoreCase, 
            "GET", 
            "HEAD");

    /// <summary>
    /// Executes the filter
    /// </summary>
    /// <param name="context">context</param>
    /// <param name="next">next</param>
    /// <returns></returns>
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var endpointIsInstanceAuthorized = context.ActionDescriptor.Properties.ContainsKey(AuthorizedEndpointKey);
        var user = context.HttpContext.User;
        if (endpointIsInstanceAuthorized && user?.Identity?.IsAuthenticated is true)
        {
            var scopeClaim = user.FindFirst("urn:altinn:scope") ?? user.FindFirst("scope");
            ProcessRequest(context, scopeClaim);
        }
        
        // Do something before the action executes.
        return next();
    }

    private static void ProcessRequest(ActionExecutingContext context, Claim? scopeClaim)
    {
        if (scopeClaim is null)
        {
            _counter.Add(1);
            return;
        }

        var scopes = scopeClaim.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (scopes.Length == 0)
        {
            _counter.Add(1);
            return;
        }

        var isRead = _readHttpMethods.Contains(context.HttpContext.Request.Method);
        var validScopes = isRead ? _acceptedReadScopes : _acceptedWriteScopes;
        if (scopes.Any(s => validScopes.Contains(s)))
        {
            return;
        }

        _counter.Add(1);
    }
}

/// <summary>
/// Custom action descriptor provider to modify the action descriptor
/// </summary>
file sealed class CustomActionDescriptorProvider : IActionDescriptorProvider
{
    /// <summary>
    /// The order of the action descriptor provider. Lower values are executed first.
    /// </summary>
    public int Order => 0;

    /// <summary>
    /// Not used
    /// </summary>
    /// <param name="context">context</param>
    public void OnProvidersExecuting(ActionDescriptorProviderContext context)
    {
    }

    /// <summary>
    /// Annotates the action descriptor with custom properties.
    /// </summary>
    /// <param name="context">context</param>
    public void OnProvidersExecuted(ActionDescriptorProviderContext context)
    {
        foreach (var action in context.Results.OfType<ControllerActionDescriptor>())
        {
            if (action.EndpointMetadata.Any(m => m is AuthorizeAttribute))
            {
                action.Properties[LogRequestsWithInvalidScopesFilter.AuthorizedEndpointKey] = "true";
            }
        }
    }
}

/// <summary>
/// Filter for requests (and child dependencies) that should not be logged.
/// </summary>
internal static class LogRequestsWithInvalidScopesDI
{
    /// <summary>
    /// Add the filter to the DI container
    /// </summary>
    /// <param name="services">The service collection</param>
    public static void AddLogRequestsWithInvalidScopesFilter(this IServiceCollection services)
    {
        services.AddSingleton<IActionDescriptorProvider, CustomActionDescriptorProvider>();
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<LogRequestsWithInvalidScopesFilter>();
        });
    }
}
