using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to instances.
    /// </summary>
    public class InstanceService : IInstanceService
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IDataService _dataService;
        private readonly IApplicationService _applicationService;
        private readonly IInstanceEventService _instanceEventService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceService"/> class.
        /// </summary>
        public InstanceService(IInstanceRepository instanceRepository, IDataService dataService, IApplicationService applicationService, IInstanceEventService instanceEventService, IApplicationRepository applicationRepository)
        {
            _instanceRepository = instanceRepository;
            _dataService = dataService;
            _applicationService = applicationService;
            _instanceEventService = instanceEventService;
            _applicationRepository = applicationRepository;
        }

        /// <inheritdoc/>
        public async Task<(bool Created, ServiceError ServiceError)> CreateSignDocument(
            int instanceOwnerPartyId, Guid instanceGuid, SignRequest signRequest, string performedBy, CancellationToken cancellationToken)
        {
            (Instance instance, long instanceInternalId) = await _instanceRepository.GetOne(instanceGuid, false, cancellationToken);
            if (instance == null) 
            {
                return (false, new ServiceError(404, "Instance not found"));
            }

            Application app = await _applicationRepository.FindOne(instance.AppId, instance.Org);

            (bool validDataType, ServiceError serviceError) = await _applicationService.ValidateDataTypeForApp(instance.Org, instance.AppId, signRequest.SignatureDocumentDataType, instance.Process.CurrentTask?.ElementId);
            if (!validDataType)
            {
                return (false, serviceError);
            }

            SignDocument signDocument = GetSignDocument(instanceGuid, signRequest);

            foreach (SignRequest.DataElementSignature dataElementSignature in signRequest.DataElementSignatures)
            {
                (string base64Sha256Hash, serviceError) = await _dataService.GenerateSha256Hash(instance.Org, instanceGuid, Guid.Parse(dataElementSignature.DataElementId), app.StorageAccountNumber);
                if (string.IsNullOrEmpty(base64Sha256Hash))
                {
                    return (false, serviceError);
                }

                signDocument.DataElementSignatures.Add(new SignDocument.DataElementSignature
                {
                    DataElementId = dataElementSignature.DataElementId,
                    Sha256Hash = base64Sha256Hash,
                    Signed = dataElementSignature.Signed
                });
            }

            DataElement dataElement = DataElementHelper.CreateDataElement(
                signRequest.SignatureDocumentDataType, 
                null, 
                instance, 
                signDocument.SignedTime, 
                "application/json", 
                $"{signRequest.SignatureDocumentDataType}.json", 
                0, 
                performedBy,
                signRequest.GeneratedFromTask);

            signDocument.Id = dataElement.Id;
        
            using (MemoryStream fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(signDocument, Formatting.Indented))))
            {
                await _dataService.UploadDataAndCreateDataElement(instance.Org, fileStream, dataElement, instanceInternalId, app.StorageAccountNumber);
            }
            
            await _instanceEventService.DispatchEvent(InstanceEventType.Signed, instance);
            return (true, null);
        }

        private static SignDocument GetSignDocument(Guid instanceGuid, SignRequest signRequest)
        {
            SignDocument signDocument = new SignDocument
            {
                InstanceGuid = instanceGuid.ToString(),
                SignedTime = DateTime.UtcNow,
                SigneeInfo = new Signee
                {
                    UserId = signRequest.Signee.UserId,
                    PersonNumber = signRequest.Signee.PersonNumber,
                    OrganisationNumber = signRequest.Signee.OrganisationNumber,
                    SystemUserId = signRequest.Signee.SystemUserId
                }
            };

            return signDocument;
        }
    }
}
