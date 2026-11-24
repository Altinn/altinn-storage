using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Authorizer for process operations. The app that owns the instance is always allowed: it commits
/// process transitions and their data on behalf of a user it has already authorized.
/// </summary>
public class ProcessAuthorizer : IProcessAuthorizer
{
    private readonly IAuthorization _authorizationService;
    private readonly IClaimsPrincipalProvider _claimsPrincipalProvider;
    private readonly GeneralSettings _generalSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessAuthorizer"/> class.
    /// </summary>
    public ProcessAuthorizer(
        IAuthorization authorizationService,
        IClaimsPrincipalProvider claimsPrincipalProvider,
        IOptions<GeneralSettings> settings
    )
    {
        _authorizationService = authorizationService;
        _claimsPrincipalProvider = claimsPrincipalProvider;
        _generalSettings = settings.Value;
    }

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeProcessNext(
        InstanceInternal instance,
        ProcessState nextProcessState
    )
    {
        ArgumentNullException.ThrowIfNull(nextProcessState);
        return Authorize(instance, nextProcessState);
    }

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeInstanceLock(InstanceInternal instance) =>
        Authorize(instance);

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeDataElementLock(InstanceInternal instance) =>
        Authorize(instance);

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizePresentationTextsUpdate(InstanceInternal instance) =>
        Authorize(instance);

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeDataValuesUpdate(InstanceInternal instance) =>
        AuthorizeWithSyncAdapterBypass(instance);

    /// <summary>
    /// Get all actions that allow process next for the given task type.
    /// </summary>
    /// <remarks>To allow process next for a custom action, user needs to have access to an action with the same name as the task type in the policy.</remarks>
    public static List<string> GetActionsThatAllowProcessNextForTaskType(string? taskType)
    {
        return taskType switch
        {
            null => [],
            "data" or "feedback" or "pdf" or "eFormidling" or "fiksArkiv" or "subformPdf" =>
            [
                AuthorizationActions.Write,
            ],
            "payment" => ["pay", AuthorizationActions.Write],
            "confirmation" => ["confirm"],
            "signing" => [AuthorizationActions.Sign, AuthorizationActions.Write],
            _ => [taskType],
        };
    }

    private Task<ProblemDetails?> AuthorizeWithSyncAdapterBypass(InstanceInternal instance)
    {
        if (_authorizationService.UserHasRequiredScope(_generalSettings.InstanceSyncAdapterScope))
        {
            return Task.FromResult<ProblemDetails?>(null);
        }

        return Authorize(instance);
    }

    private async Task<ProblemDetails?> Authorize(InstanceInternal instance)
    {
        if (IsCalledByApp(instance))
        {
            return null;
        }

        string? taskId = instance.Process?.CurrentTask?.ElementId;
        string? altinnTaskType = instance.Process?.CurrentTask?.AltinnTaskType;

        List<string> actions = instance.Process?.CurrentTask is null
            ? [AuthorizationActions.Write]
            : GetActionsThatAllowProcessNextForTaskType(altinnTaskType);

        // we don't know if this is an abandon flow, so we include "reject" as a fallback.
        actions.Add(AuthorizationActions.Reject);

        return await AuthorizeAnyAction(instance, actions, taskId);
    }

    private async Task<ProblemDetails?> Authorize(
        InstanceInternal instance,
        ProcessState nextProcessState
    )
    {
        if (IsCalledByApp(instance))
        {
            return null;
        }

        if (instance.Process?.CurrentTask is null)
        {
            return AuthorizationProblems.Forbidden();
        }

        string? taskId = instance.Process.CurrentTask.ElementId;
        string? altinnTaskType = instance.Process.CurrentTask.AltinnTaskType;

        if (nextProcessState.CurrentTask?.FlowType == "AbandonCurrentMoveToNext")
        {
            return await _authorizationService.AuthorizeInstanceAction(
                instance,
                AuthorizationActions.Reject,
                taskId
            );
        }

        if (
            nextProcessState.CurrentTask?.FlowType is not null
            && nextProcessState.CurrentTask.FlowType != "CompleteCurrentMoveToNext"
        )
        {
            altinnTaskType = nextProcessState.CurrentTask.AltinnTaskType;
            taskId = nextProcessState.CurrentTask.ElementId;
        }

        List<string> actions = GetActionsThatAllowProcessNextForTaskType(altinnTaskType);

        return await AuthorizeAnyAction(instance, actions, taskId);
    }

    /// <summary>
    /// Whether the caller is the app that owns the instance. Storage cannot yet tell an app token
    /// from other service owner tokens, so any token for the org that owns the app qualifies.
    /// </summary>
    /// <summary>
    /// Authorizes the first of <paramref name="actions"/> that Altinn Authorization permits. When none is
    /// permitted, the answer is 503 if it gave no decision on any of them, otherwise 403.
    /// </summary>
    private async Task<ProblemDetails?> AuthorizeAnyAction(
        InstanceInternal instance,
        List<string> actions,
        string? taskId
    )
    {
        ProblemDetails problem = AuthorizationProblems.Forbidden();
        foreach (string action in actions)
        {
            ProblemDetails? actionProblem = await _authorizationService.AuthorizeInstanceAction(
                instance,
                action,
                taskId
            );
            if (actionProblem is null)
            {
                return null;
            }

            if (actionProblem.Status == StatusCodes.Status503ServiceUnavailable)
            {
                problem = actionProblem;
            }
        }

        return problem;
    }

    private bool IsCalledByApp(InstanceInternal instance) =>
        _claimsPrincipalProvider.GetUser().GetOrg() is { } org && org == instance.Org;
}
