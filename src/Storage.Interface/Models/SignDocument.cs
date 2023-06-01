using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#nullable enable
namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Signature document with list of signed/unsigned dataElements
    /// </summary>
    public class SignDocument
    {
        /// <summary>
        /// Unique id of the SignDocument (identical to dataElementId for this document).
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Instance Id
        /// </summary>
        [JsonProperty(PropertyName = "instanceGuid")]
        public string InstanceGuid { get; set; } = string.Empty;  

        /// <summary>
        /// Timestamp for when the document was signed
        /// </summary>
        [JsonProperty(PropertyName = "signedTime")]
        public DateTime SignedTime { get; set; }
        
        /// <summary>
        /// Information about the signee
        /// </summary>
        [JsonProperty(PropertyName = "signeeInfo")]
        public Signee SigneeInfo { get; set; } = new Signee();

        /// <summary>
        /// List of dataElementSignatures
        /// </summary>
        [JsonProperty(PropertyName = "dataElementSignatures")]
        public List<DataElementSignature> DataElementSignatures { get; set; } = new List<DataElementSignature>();
        
        /// <summary>
        /// The DataElementSignature
        /// </summary>
        public class DataElementSignature
        {
            /// <summary>
            /// Id of the dataElement.
            /// </summary>
            [JsonProperty(PropertyName = "dataElementId")]
            public string DataElementId { get; set; } = string.Empty;

            /// <summary>
            /// Md5 hash of the dataelement
            /// </summary>
            [JsonProperty(PropertyName = "md5Hash")]
            public string Md5Hash { get; set; } = string.Empty;

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
            /// The userId representing the signee
            /// </summary>
            [JsonProperty(PropertyName = "userId")]
            public string UserId { get; set; } = string.Empty;

            /// <summary>
            /// The partyId representing the signee
            /// </summary>
            [JsonProperty(PropertyName = "partyId")]
            public string PartyId { get; set; } = string.Empty;

            /// <summary>
            /// The personNumber representing the signee
            /// </summary>
            [JsonProperty(PropertyName = "personNumber")]
            public string? PersonNumber { get; set; }

            /// <summary>
            /// The organisationNumber representing the signee
            /// </summary>
            [JsonProperty(PropertyName = "organisationNumber")]
            public string? OrganisationNumber { get; set; }
        }
    }
}