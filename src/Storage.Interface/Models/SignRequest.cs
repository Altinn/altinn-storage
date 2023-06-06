using System.Collections.Generic;

using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents all dataElements with signing status
    /// </summary>
    public class SignRequest
    {
        /// <summary>
        /// The data type to use of the generated signature document
        /// </summary>
        [JsonProperty(PropertyName = "signatureDocumentDataType")]
        public string SignatureDocumentDataType { get; set; }

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
            /// Signing status for dataElement.
            /// </summary>
            [JsonProperty(PropertyName = "signed")]
            public bool Signed { get; set; }
        }
    }
}