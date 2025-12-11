using System.Text.Json;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public static class InstanceExtentions
{
    public static Instance Clone(this Instance instance)
    {
        return JsonSerializer.Deserialize<Instance>(JsonSerializer.Serialize(instance));
    }
}
