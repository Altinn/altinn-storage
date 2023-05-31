using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Signature document with list of signed/unsigned dataElements
    /// </summary>
    public class SignDocument
    {
        /// <summary>
        /// Unique id of the SignDocument (DataElementId).
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        /// <summary>
        /// Instance Id
        /// </summary>
        [JsonProperty(PropertyName = "instanceGuid")]
        public string InstanceGuid { get; set; }  

        /// <summary>
        /// Timestamp for when the document was signed
        /// </summary>
        [JsonProperty(PropertyName = "signedTime")]
        public DateTime SignedTime { get; set; }
        
        /// <summary>
        /// List of dataElementSignatures
        /// </summary>
        [JsonProperty(PropertyName = "dataElementSignatures")]
        public List<DataElementSignature> DataElementSignatures { get; set; }
        
        /// <summary>
        /// The DataElementSignature
        /// </summary>
        public class DataElementSignature
        {
            /// <summary>
            /// Id of the dataElement.
            /// </summary>
            [JsonProperty(PropertyName = "dataElementId")]
            public string DataElementId { get; set; }

            /// <summary>
            /// Md5 hash of the dataelement
            /// </summary>
            [JsonProperty(PropertyName = "md5Hash")]
            public string Md5Hash { get; set; }

            /// <summary>
            /// Signing status for dataElement.
            /// </summary>
            [JsonProperty(PropertyName = "signed")]
            public bool Signed { get; set; }
        }

        /// <summary>
        /// Information about the signee
        /// </summary>
        public class Signee
        {
            /// <summary>
            /// UserId
            /// </summary>
            [JsonProperty(PropertyName = "userId")]
            public string UserId { get; set; }

            /// <summary>
            /// PartyId
            /// </summary>
            [JsonProperty(PropertyName = "partyId")]
            public string PartyId { get; set; }

            /// <summary>
            /// PersonNumber
            /// </summary>
            [JsonProperty(PropertyName = "personNumber")]
            public string? PersonNumber { get; set; }

            /// <summary>
            /// OrganisationNumber
            /// </summary>
            [JsonProperty(PropertyName = "organisationNumber")]
            public string? OrganisationNumber { get; set; }
        }
    }
}