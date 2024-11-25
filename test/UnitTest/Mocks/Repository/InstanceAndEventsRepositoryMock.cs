using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Repository
{
    public class InstanceAndEventsRepositoryMock : IInstanceAndEventsRepository
    {
        public Task<Instance> Update(Instance instance, List<string> updateProperties, List<InstanceEvent> events)
        {
            if (instance.Id.Equals("1337/d3b326de-2dd8-49a1-834a-b1d23b11e540"))
            {
                return Task.FromResult<Instance>(null);
            }

            instance.Data = new List<DataElement>();

            return Task.FromResult(instance);
        }
    }
}
