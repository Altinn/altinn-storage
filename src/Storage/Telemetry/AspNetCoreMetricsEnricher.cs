#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.AccessManagement.Core.Models;
using Altinn.Platform.Storage.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Enriches the 'http.server.request.duration' metric
/// to indicate how many requests are made to an authorized endpoint without proper scopes.
/// </summary>
internal sealed class AspNetCoreMetricsEnricher(ILogger<AspNetCoreMetricsEnricher> logger) : IAsyncActionFilter
{
    /// <summary>
    /// Name of the metric that is enriched
    /// </summary>
    public const string MetricName = "http.server.request.duration";

    private readonly ILogger<AspNetCoreMetricsEnricher> _logger = logger;

    /// <summary>
    /// A key to indicate if the endpoint is authorized
    /// </summary>
    internal static readonly object AllowedScopesKey = new();

    /// <summary>
    /// Executes the filter
    /// </summary>
    /// <param name="context">context</param>
    /// <param name="next">next</param>
    /// <returns></returns>
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var feature = context.HttpContext.Features.Get<IHttpMetricsTagsFeature>();
        var user = context.HttpContext.User;
        if (feature is null || user is null)
        {
            return next();
        }

        var clientId = user.FindFirstValue("client_id");
        var consumer = user.FindFirstValue("consumer");
        string? consumerId = null;
        if (!string.IsNullOrWhiteSpace(consumer))
        {
            try 
            {
                var orgClaim = JsonSerializer.Deserialize<OrgClaim>(consumer);
                consumerId = orgClaim?.ID;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize consumer claim in token");
            }
        }

        feature.Tags.Add(new KeyValuePair<string, object?>("client.id", clientId));
        feature.Tags.Add(new KeyValuePair<string, object?>("client.consumer.id", consumerId));

        if (context.ActionDescriptor.Properties.TryGetValue(AllowedScopesKey, out var allowedScopesObj) && user.Identity?.IsAuthenticated is true)
        {
            Debug.Assert(allowedScopesObj is FrozenSet<string>);
            FrozenSet<string> allowedScopes = (FrozenSet<string>)allowedScopesObj!;
            ValidateScope(allowedScopes, feature, context.HttpContext);
        }

        return next();
    }

    private void ValidateScope(FrozenSet<string> allowedScopes, IHttpMetricsTagsFeature feature, HttpContext httpContext)
    {
        var user = httpContext.User;
        Debug.Assert(user is not null);
        var scopeClaim = user.FindFirst("urn:altinn:scope") ?? user.FindFirst("scope");
        if (scopeClaim is null)
        {
            Report(feature);
            return;
        }

        var scopes = scopeClaim.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (scopes.Length == 0)
        {
            Report(feature);
            return;
        }

        if (!scopes.Any(s => allowedScopes.Contains(s)))
        {
            Report(feature);
            return;
        }

        static void Report(IHttpMetricsTagsFeature feature) =>
            feature.Tags.Add(new KeyValuePair<string, object?>("invalid_scopes", true));
    }
}

/// <summary>
/// Custom action descriptor provider to modify the action descriptor
/// </summary>
internal sealed class CustomActionDescriptorProvider : IActionDescriptorProvider
{
    /// <summary>
    /// The order of the action descriptor provider. Lower values are executed first.
    /// </summary>
    public int Order => 0;

    private List<ControllerActionDescriptor>? _actionsToValidate;
    private List<ControllerActionDescriptor>? _actionsNotValidated;

    /// <summary>
    /// The actions that need instances scope validation
    /// </summary>
    public IReadOnlyList<ControllerActionDescriptor>? ActionsToValidate => _actionsToValidate;

    /// <summary>
    /// The actions that do not need instances scope validation
    /// </summary>
    public IReadOnlyList<ControllerActionDescriptor>? ActionsNotValidated => _actionsNotValidated;

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

    private static readonly FrozenSet<string> _manuallyIncludeActions = 
        FrozenSet.Create<string>(
            StringComparer.Ordinal,
            "Altinn.Platform.Storage.Controllers.DataLockController.Unlock (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.InstancesController.GetInstances (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.InstancesController.Post (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.InstancesController.UpdateSubstatus (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.ProcessController.PutInstanceAndEvents (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.ProcessController.PutProcess (Altinn.Platform.Storage)");

    private static readonly FrozenSet<string> _manuallyExcludeActions = 
        FrozenSet.Create<string>(
            StringComparer.Ordinal,
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.GetMessageBoxInstance (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.SearchMessageBoxInstances (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.Delete (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.GetMessageBoxInstance (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.GetMessageBoxInstanceEvents (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.SearchMessageBoxInstances (Altinn.Platform.Storage)",
            "Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.Undelete (Altinn.Platform.Storage)");

    private static readonly FrozenDictionary<string, FrozenSet<string>> _scopesOverride = new Dictionary<string, FrozenSet<string>>()
    {
        ["Altinn.Platform.Storage.Controllers.MessageBoxInstancesController.SearchMessageBoxInstances (Altinn.Platform.Storage)"] = _acceptedReadScopes,
    }.ToFrozenDictionary();

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
        _actionsToValidate = new List<ControllerActionDescriptor>();
        _actionsNotValidated = new List<ControllerActionDescriptor>();

        foreach (var action in context.Results.OfType<ControllerActionDescriptor>())
        {
            var isManuallyExcluded = _manuallyExcludeActions.Contains(action.DisplayName ?? string.Empty);
            if (isManuallyExcluded)
            {
                _actionsNotValidated.Add(action);
                continue;
            }

            var authorizeAttr = (AuthorizeAttribute?)action.EndpointMetadata.FirstOrDefault(m => m is AuthorizeAttribute);
            var authorizePolicy = authorizeAttr?.Policy;
            var isManuallyIncluded = _manuallyIncludeActions.Contains(action.DisplayName ?? string.Empty);
            if (isManuallyIncluded)
            {
                ProcessAction(action, authorizePolicy);
                continue;
            }

            var authorizeAttrHasInstancePolicy = authorizePolicy?.Contains("Instance", StringComparison.Ordinal) is true;
            var hasAllowAnonymousAttr = action.EndpointMetadata.Any(m => m is AllowAnonymousAttribute);
            if (authorizeAttrHasInstancePolicy && !hasAllowAnonymousAttr)
            {
                ProcessAction(action, authorizePolicy);
            }
            else
            {
                _actionsNotValidated.Add(action);
            }
        }
    }

    private void ProcessAction(ControllerActionDescriptor action, string? authorizePolicy)
    {   
        Debug.Assert(_actionsToValidate is not null);
        FrozenSet<string> scopes;
        if (_scopesOverride.TryGetValue(action.DisplayName ?? string.Empty, out var overrideScopes))
        {
            scopes = overrideScopes;
        }
        else if (authorizePolicy is not null)
        {
            scopes = authorizePolicy == AuthzConstants.POLICY_INSTANCE_READ ? _acceptedReadScopes : _acceptedWriteScopes;
        }
        else
        {
            var httpMethodAttr = (HttpMethodAttribute?)action.EndpointMetadata.FirstOrDefault(m => m is HttpMethodAttribute);
            if (httpMethodAttr is not null && httpMethodAttr.HttpMethods.Any(m => _readHttpMethods.Contains(m)))
            {
                scopes = _acceptedReadScopes;
            }
            else
            {
                scopes = _acceptedWriteScopes;
            }
        }

        action.Properties[AspNetCoreMetricsEnricher.AllowedScopesKey] = scopes;
        _actionsToValidate.Add(action);
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
    public static void AddAspNetCoreMetricsEnricher(this IServiceCollection services)
    {
        services.AddSingleton<CustomActionDescriptorProvider>();
        services.AddSingleton<IActionDescriptorProvider>(sp => sp.GetRequiredService<CustomActionDescriptorProvider>());
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<AspNetCoreMetricsEnricher>();
        });
    }
}
