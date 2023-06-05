using System;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to instances.
    /// </summary>
    public class InstanceService : IInstanceService
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventService _instanceEventService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceService"/> class.
        /// </summary>
        public InstanceService(IInstanceRepository instanceRepository, IInstanceEventService instanceEventService)
        {
            _instanceRepository = instanceRepository;
            _instanceEventService = instanceEventService;
        }

        /// <inheritdoc/>
        public async Task<SignDocument> CreateSignDocument(int instanceOwnerPartyId, Guid instanceGuid, SignRequest signRequest)
        {
            Instance instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);

            // handle logic for signing
            await _instanceEventService.DispatchEvent(InstanceEventType.Signed, instance);
            return null;
        }
    }
}