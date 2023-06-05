using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to instances.
    /// </summary>
    public class InstanceService : IInstanceService
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IDataService _dataService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceService"/> class.
        /// </summary>
        public InstanceService(IInstanceRepository instanceRepository, IDataService dataService)
        {
            _instanceRepository = instanceRepository;
            _dataService = dataService;
        }

        /// <inheritdoc/>
        public async Task<SignDocument> CreateSignDocument(int instanceOwnerPartyId, Guid instanceGuid, SignRequest signRequest)
        {
            Instance instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);
            if (instance == null) 
            {
                // TODO: Return some shit.. ActionResult out from controller
            }
            SignDocument signDocument = new SignDocument();

            foreach(SignRequest.DataElementSignature dataElementSignature in signRequest.DataElementSignatures)
            {
                // String md5Hash = await _dataService.GenerateMd5Hash(instance.Org, instanceGuid, Guid.Parse(dataElementSignature.DataElementId));
            }
            throw new System.NotImplementedException();
        }
    }
}