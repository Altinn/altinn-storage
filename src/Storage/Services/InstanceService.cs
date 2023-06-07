using System;
using System.Threading.Tasks;
using Altinn.Platform.Profile.Models;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to instances.
    /// </summary>
    public class InstanceService : IInstanceService
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IDataService _dataService;
        private readonly IProfileService _profileService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceService"/> class.
        /// </summary>
        public InstanceService(IInstanceRepository instanceRepository, IDataService dataService, IProfileService profileService)
        {
            _instanceRepository = instanceRepository;
            _dataService = dataService;
            _profileService = profileService;
        }

        /// <inheritdoc/>
        public async Task CreateSignDocument(int instanceOwnerPartyId, Guid instanceGuid, SignRequest signRequest, UserContext userContext)
        {
            Instance instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);
            if (instance == null) 
            {
                // TODO: Return some shit.. ActionResult out from controller
            }

            SignDocument signDocument = await GetSignDocument(instanceGuid, userContext);

            foreach (SignRequest.DataElementSignature dataElementSignature in signRequest.DataElementSignatures)
            {
                string bas64Sha256Hash = await _dataService.GenerateSha256Hash(instance.Org, instanceGuid, Guid.Parse(dataElementSignature.DataElementId));
                signDocument.DataElementSignatures.Add(new SignDocument.DataElementSignature
                {
                    DataElementId = dataElementSignature.DataElementId,
                    Sha256Hash = bas64Sha256Hash,
                    Signed = dataElementSignature.Signed
                });
            }
            // create dataelement
            // save signdocument blob
        }

        private async Task<SignDocument> GetSignDocument(Guid instanceGuid, UserContext userContext)
        {
            SignDocument signDocument = new SignDocument
            {
                InstanceGuid = instanceGuid.ToString(),
                SigneeInfo = new SignDocument.Signee
                {
                    UserId = userContext.UserId.ToString(),
                    PartyId = userContext.PartyId,
                    OrganisationNumber = userContext.Orgnr.ToString()
                }
            };

            if (userContext.Orgnr == null)
            {
                UserProfile userProfile = await _profileService.GetUserProfile(userContext.UserId.Value);
                signDocument.SigneeInfo.PersonNumber = userProfile.Party.SSN;
            }

            return signDocument;
        }
    }
}