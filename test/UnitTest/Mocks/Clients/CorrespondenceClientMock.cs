using System;
using System.Linq;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Clients;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Clients
{
    public class CorrespondenceClientMock : ICorrespondenceClient
    {
        private readonly string[] _eventTypes = ["read", "confirm", "delete"];

        public async Task SyncCorrespondenceEvent(int correspondenceId, int partyId, DateTimeOffset eventTimestamp, string eventType)
        {
            switch (correspondenceId)
            {
                case 2674:
                    await Task.CompletedTask;
                    break;
                case 2675:
                    if (partyId == 1337)
                    {
                        throw new ArgumentException("Unknown party id");
                    }
                    else if (!_eventTypes.Contains(eventType))
                    {
                        throw new ArgumentException("Invalid event type");
                    }
                    else
                    {
                        throw new ArgumentException("Unknown correspondenceId");
                    }

                default:
                    throw new ArgumentException("Unknown correspondenceId");
            }
        }
    }
}
