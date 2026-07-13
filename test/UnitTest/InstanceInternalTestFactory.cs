#nullable disable

using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.UnitTest;

internal static class InstanceInternalTestFactory
{
    internal static InstanceInternal Create(
        Instance instance,
        IReadOnlyList<DataElementInternal> data,
        long InternalId
    )
    {
        InstanceInternal result = instance.FromApiModel();
        result.Data =
            data is null ? null
            : data is List<DataElementInternal> list ? list
            : [.. data];
        result.InternalId = InternalId;
        return result;
    }
}
