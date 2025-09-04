using System;
using System.Linq;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Clients;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Clients
{
    public class CorrespondenceClientMock : ICorrespondenceClient
    {
        private readonly string[] _eventTypes = ["read", "confirm", "delete"];

        /// <summary>
        /// Simulates syncing a correspondence event for unit tests.
        /// </summary>
        /// <remarks>
        /// This mock validates inputs and either completes successfully for known test data or throws an ArgumentException for specific invalid conditions:
        /// - correspondenceId 2674: completes successfully.
        /// - correspondenceId 2675: throws if partyId equals 1337 ("Unknown party id"), or if eventType is not one of "read", "confirm", or "delete" ("Invalid event type"); otherwise throws "Unknown correspondenceId".
        /// - any other correspondenceId: throws "Unknown correspondenceId".
        /// The <paramref name="eventTimestamp"/> parameter is accepted but not used by this mock.
        /// </remarks>
        /// <param name="correspondenceId">The correspondence identifier used to select the mock behavior.</param>
        /// <param name="partyId">The party identifier used for validation in certain test scenarios.</param>
        /// <param name="eventTimestamp">Timestamp of the event; not used by this mock implementation.</param>
        /// <param name="eventType">The event type to validate; expected values are "read", "confirm", or "delete".</param>
        /// <returns>A task that completes when the mock action finishes.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid partyId, invalid eventType, or unknown correspondenceId as described above.</exception>
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
