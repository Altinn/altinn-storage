using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Authorizer for process operations. To keep app policies simple, the app that owns the instance skips
/// policy evaluation: its workflow engine commits the app's process transitions. Storage cannot yet tell
/// an app token from other service owner tokens, so any token for the org that owns the app qualifies.
/// Every other caller is checked as each method describes.
/// </summary>
public interface IProcessAuthorizer
{
    /// <summary>
    /// Determines if the user is authorized to perform process next for the current task.
    /// Checks authorization against the set of actions that allow process next for the current task type.
    /// An instance without a current task (an ended or not-started process) admits only the app.
    /// </summary>
    /// <param name="instance">The instance to authorize against.</param>
    /// <param name="nextProcessState">The incoming process state, used to handle flow type overrides (e.g. AbandonCurrentMoveToNext).</param>
    Task<bool> AuthorizeProcessNext(InstanceInternal instance, ProcessState nextProcessState);

    /// <summary>
    /// Determines if the user is authorized to lock an instance.
    /// Checks the task-type actions plus "reject", since the flow type is not known at lock time.
    /// </summary>
    Task<bool> AuthorizeInstanceLock(InstanceInternal instance);

    /// <summary>
    /// Determines if the user is authorized to lock a data element.
    /// Checks the task-type actions plus "reject", since the flow type is not known at lock time.
    /// </summary>
    Task<bool> AuthorizeDataElementLock(InstanceInternal instance);

    /// <summary>
    /// Determines if the user is authorized to update presentation texts.
    /// Checks the task-type actions plus "reject", since the flow type is not known at update time.
    /// </summary>
    Task<bool> AuthorizePresentationTextsUpdate(InstanceInternal instance);

    /// <summary>
    /// Determines if the user is authorized to update data values.
    /// Checks the task-type actions plus "reject", since the flow type is not known at update time.
    /// </summary>
    Task<bool> AuthorizeDataValuesUpdate(InstanceInternal instance);
}
