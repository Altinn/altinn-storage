using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Utility methods for DataType objects.
/// </summary>
internal static class DataTypeExtensions
{
    /// <summary>
    /// Checks if the user has permission to read data of this type for the given storage instance.
    /// </summary>
    public static async Task<bool> CanRead(
        this DataType dataType,
        IAuthorization authorizationService,
        InstanceInternal instance,
        string? task = null
    )
    {
        if (string.IsNullOrWhiteSpace(dataType.ActionRequiredToRead))
        {
            return true;
        }

        return await authorizationService.AuthorizeInstanceAction(
            instance,
            dataType.ActionRequiredToRead,
            task ?? instance.Process?.CurrentTask?.ElementId
        );
    }

    /// <summary>
    /// Checks if a user who is not the caller has permission to read data of this type for the
    /// storage instance.
    /// </summary>
    public static async Task<bool> CanReadForUser(
        this DataType dataType,
        IAuthorization authorizationService,
        InstanceInternal instance,
        UserSubject subject
    )
    {
        if (string.IsNullOrWhiteSpace(dataType.ActionRequiredToRead))
        {
            return true;
        }

        return await authorizationService.AuthorizeInstanceActionForUser(
            instance,
            dataType.ActionRequiredToRead,
            instance.Process?.CurrentTask?.ElementId,
            subject
        );
    }

    /// <summary>
    /// Checks if the user has permission to write data of this type for the given storage instance.
    /// </summary>
    public static async Task<bool> CanWrite(
        this DataType dataType,
        IAuthorization authorizationService,
        InstanceInternal instance,
        string? task = null
    )
    {
        if (string.IsNullOrWhiteSpace(dataType.ActionRequiredToWrite))
        {
            return true;
        }

        return await authorizationService.AuthorizeInstanceAction(
            instance,
            dataType.ActionRequiredToWrite,
            task ?? instance.Process?.CurrentTask?.ElementId
        );
    }
}
