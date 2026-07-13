#nullable disable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository;

public class InstanceAndEventsRepositoryMock : IInstanceAndEventsRepository
{
    public Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        List<InstanceEvent> events,
        CancellationToken cancellationToken
    )
    {
        if (instance.Id.Equals("d3b326de-2dd8-49a1-834a-b1d23b11e540"))
        {
            return Task.FromResult<InstanceInternal>(null);
        }

        instance.Data = [];

        return Task.FromResult(instance);
    }
}
